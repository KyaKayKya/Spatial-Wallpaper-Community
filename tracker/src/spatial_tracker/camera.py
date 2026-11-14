"""Windows-only shared read-only capture, addressed by device ID rather than index."""

import asyncio
import hashlib
import sys
import threading

from .clock import qpc_us


def camera_id(device_path):
    return "winrt-" + hashlib.sha256(device_path.casefold().encode()).hexdigest()[:32]


def describe_format(source, fmt):
    w, h = fmt.video_format.width, fmt.video_format.height
    n, d = fmt.frame_rate.numerator, fmt.frame_rate.denominator
    key = f'{source.info.id}|{w}|{h}|{n}|{d}|{fmt.subtype}'
    return {'mode_id': 'winrt-mode-' + hashlib.sha256(key.encode()).hexdigest()[:32],
            'width_px': w, 'height_px': h, 'fps': n/d if d else 0,
            'fps_numerator': n, 'fps_denominator': d, 'pixel_format': fmt.subtype}


def select_format(sources, mode_id):
    for source in sources:
        for fmt in source.supported_formats:
            if describe_format(source, fmt)['mode_id'] == mode_id:
                return source, fmt
    raise CaptureError('UNSUPPORTED_CAPABILITY', 'Selected camera mode is unsupported or removed')


def bitmap_to_rgb(bitmap):
    import numpy as np
    from winrt.windows.graphics.imaging import SoftwareBitmap, BitmapPixelFormat, BitmapAlphaMode
    from winrt.windows.storage.streams import Buffer

    converted = SoftwareBitmap.convert_with_alpha(
        bitmap, BitmapPixelFormat.RGBA8, BitmapAlphaMode.IGNORE
    )
    try:
        w, h = converted.pixel_width, converted.pixel_height
        buffer = Buffer(w * h * 4)
        converted.copy_to_buffer(buffer)
        return np.frombuffer(buffer, dtype=np.uint8).reshape(h, w, 4)[:, :, :3].copy()
    finally:
        converted.close()


class CaptureError(RuntimeError):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def reader_start_error(status):
    code = {
        "EXCLUSIVE_CONTROL_NOT_AVAILABLE": "CAMERA_BUSY",
        "DEVICE_NOT_AVAILABLE": "CAMERA_UNAVAILABLE",
        "OUTPUT_FORMAT_NOT_SUPPORTED": "UNSUPPORTED_CAPABILITY",
    }.get(status, "CAPTURE_FAILED")
    return CaptureError(code, "Camera reader start failed: " + status)


class WindowsCamera:
    supports_modes = True

    def __init__(self):
        if sys.platform != "win32":
            raise RuntimeError("Windows capture requires Windows")
        self._capture = None
        self._reader = None
        self._tokens = []
        self._watcher = None
        self._watch_tokens = []
        self._watch_stopped = None
        self._paths = {}
        self._frame_lock = threading.Lock()
        self._pending = None
        self._scheduled = False
        self._generation = 0
        self._loop = asyncio.get_running_loop()
        self.supported_modes = []
        self._mode_catalog = {}

    async def catalog(self):
        from winrt.windows.devices.enumeration import DeviceClass, DeviceInformation

        devices = await DeviceInformation.find_all_async_device_class(DeviceClass.VIDEO_CAPTURE)
        self._paths = {camera_id(item.id): item.id for item in devices}
        return [
            {
                "camera_id": camera_id(item.id),
                "display_name": item.name,
                "present": True,
                "backend": "windows_media_capture",
                "modes": self._mode_catalog.get(camera_id(item.id), []),
                "capabilities": {
                    "stable_selection": True,
                    "device_notifications": True,
                    "sharing": "shared_read_only",
                    "external_usage_detection": "conflict_only",
                    "automatic_yield": "conflict_only",
                    "compatibility_status": "unverified",
                    "compatibility_evidence_id": None,
                    "optional_features": [],
                },
            }
            for item in devices
        ]

    def watch(self, changed):
        from winrt.windows.devices.enumeration import DeviceClass, DeviceInformation

        self._watcher = DeviceInformation.create_watcher_device_class(DeviceClass.VIDEO_CAPTURE)
        self._watch_stopped = self._loop.create_future()

        def stopped(*_):
            def complete():
                if not self._watch_stopped.done():
                    self._watch_stopped.set_result(None)

            self._loop.call_soon_threadsafe(complete)

        token = self._watcher.add_stopped(stopped)
        self._watch_tokens.append(("stopped", token))
        for kind in ("added", "removed", "updated"):

            def callback(*_):
                self._loop.call_soon_threadsafe(changed)

            token = getattr(self._watcher, "add_" + kind)(callback)
            self._watch_tokens.append((kind, token))
        self._watcher.start()

    async def stop_watch(self):
        if self._watcher:
            self._watcher.stop()
            # Stop returns before native event delivery has drained. Keep the
            # watcher and Python delegates alive until its Stopped notification.
            await self._watch_stopped
            for kind, token in self._watch_tokens:
                getattr(self._watcher, "remove_" + kind)(token)
            self._watch_tokens.clear()
            self._watcher = None
            self._watch_stopped = None

    async def open(self, selected, on_frame, on_error, mode_id=None):
        from winrt.windows.media.capture import (
            MediaCapture,
            MediaCaptureInitializationSettings,
            MediaCaptureSharingMode,
            StreamingCaptureMode,
            MediaCaptureMemoryPreference,
            MediaCaptureDeviceExclusiveControlStatus,
        )
        from winrt.windows.media.capture.frames import (
            MediaFrameSourceKind,
            MediaFrameReaderStartStatus,
            MediaFrameReaderAcquisitionMode,
        )

        await self.close()
        await self.catalog()
        if selected not in self._paths:
            raise CaptureError("CAMERA_UNAVAILABLE", "Selected camera is absent")
        settings = MediaCaptureInitializationSettings()
        settings.video_device_id = self._paths[selected]
        settings.streaming_capture_mode = StreamingCaptureMode.VIDEO
        settings.sharing_mode = (MediaCaptureSharingMode.EXCLUSIVE_CONTROL if mode_id is not None
                                 else MediaCaptureSharingMode.SHARED_READ_ONLY)
        settings.memory_preference = MediaCaptureMemoryPreference.CPU
        self._capture = MediaCapture()
        capture = self._capture
        generation = self._generation

        def fail(_sender, args):
            # Device errors are facts; no raw native diagnostics enter the protocol.
            code = args.code & 0xFFFFFFFF
            mapped = "CAMERA_BUSY" if code == 0x80070020 else "CAPTURE_FAILED"
            exc = CaptureError(mapped, f"Capture failed: 0x{code:08x}")
            self._loop.call_soon_threadsafe(on_error, exc)

        def exclusive_changed(_sender, args):
            if (
                generation == self._generation
                and args.status
                == MediaCaptureDeviceExclusiveControlStatus.SHARED_READ_ONLY_AVAILABLE
            ):
                self._loop.call_soon_threadsafe(
                    on_error, CaptureError("CAMERA_BUSY", "Another app acquired exclusive control")
                )

        self._tokens.append(("failed", capture.add_failed(fail)))
        try:
            await capture.initialize_with_settings_async(settings)
            # This event requires an initialized MediaCapture on native Windows drivers.
            self._tokens.append(
                (
                    "capture_device_exclusive_control_status_changed",
                    capture.add_capture_device_exclusive_control_status_changed(exclusive_changed),
                )
            )
            sources = [
                source
                for source in capture.frame_sources.values()
                if source.info.source_kind == MediaFrameSourceKind.COLOR
            ]
            if not sources:
                raise CaptureError("CAPTURE_FAILED", "Camera exposes no color frame source")
            unique = {}
            for item in sources:
                for native in item.supported_formats:
                    description = describe_format(item, native)
                    if description['fps'] > 0:
                        unique[description['mode_id']] = description
            self.supported_modes = sorted(unique.values(), key=lambda m: (
                -m['width_px']*m['height_px'], -m['fps'], m['mode_id']))
            self._mode_catalog[selected] = self.supported_modes
            source = sources[0]
            if mode_id is not None:
                source, requested = select_format(sources, mode_id)
                await source.set_format_async(requested)
                if describe_format(source, source.current_format)['mode_id'] != mode_id:
                    raise CaptureError('UNSUPPORTED_CAPABILITY', 'Driver did not apply requested mode')
            fmt = source.current_format
            width, height = fmt.video_format.width, fmt.video_format.height
            rate = fmt.frame_rate
            fps = rate.numerator / rate.denominator if rate.denominator else 0
            self._reader = await capture.create_frame_reader_with_subtype_async(source, "BGRA8")
            self._reader.acquisition_mode = MediaFrameReaderAcquisitionMode.REALTIME

            def arrived(sender, _args):
                if generation != self._generation or not self._frame_lock.acquire(False):
                    return
                reference = bitmap = None
                try:
                    reference = sender.try_acquire_latest_frame()
                    if reference is None:
                        return
                    stamp = qpc_us()
                    bitmap = reference.video_media_frame.software_bitmap
                    if bitmap is None:
                        return
                    rgb = bitmap_to_rgb(bitmap)
                    # At most one copied frame can wait on the event loop.
                    self._pending = (generation, rgb, stamp)
                    if not self._scheduled:
                        self._scheduled = True
                        self._loop.call_soon_threadsafe(deliver)
                except Exception:
                    self._loop.call_soon_threadsafe(
                        on_error, CaptureError("CAPTURE_FAILED", "Unable to read camera frame")
                    )
                finally:
                    for resource in (bitmap, reference):
                        if resource is not None:
                            resource.close()
                    self._frame_lock.release()

            def deliver():
                with self._frame_lock:
                    pending, self._pending = self._pending, None
                    self._scheduled = False
                if pending and pending[0] == self._generation:
                    _, rgb, stamp = pending
                    on_frame(rgb, stamp)

            self._frame_token = self._reader.add_frame_arrived(arrived)
            status = await self._reader.start_async()
            if status != MediaFrameReaderStartStatus.SUCCESS:
                raise reader_start_error(status.name)
            return {
                "mode_id": mode_id,
                "source_width_px": width,
                "source_height_px": height,
                "width_px": width,
                "height_px": height,
                "fps": fps,
                "pixel_format": "RGB8",
                "rotation_deg": 0,
                "crop_rect": [0, 0, width, height],
                "mirrored": False,
            }
        except OSError as exc:
            code = exc.winerror & 0xFFFFFFFF if exc.winerror else 0
            mapped = "PERMISSION_DENIED" if code == 0x80070005 else "CAPTURE_FAILED"
            if code == 0x80070020:
                mapped = "CAMERA_BUSY"
            raise CaptureError(mapped, f"Camera initialization failed: 0x{code:08x}") from exc

    async def close(self):
        self._generation += 1
        reader, capture = self._reader, self._capture
        self._reader = self._capture = None
        try:
            if reader is not None:
                if hasattr(self, "_frame_token"):
                    reader.remove_frame_arrived(self._frame_token)
                try:
                    await reader.stop_async()
                finally:
                    reader.close()
        finally:
            if capture is not None:
                for kind, token in self._tokens:
                    getattr(capture, "remove_" + kind)(token)
                self._tokens.clear()
                capture.close()
            with self._frame_lock:
                self._pending = None

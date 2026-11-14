"""In-process AVFoundation adapter. Native objects remain behind a small C ABI."""

import asyncio
import ctypes
import json
import os
from pathlib import Path
import threading

from .camera import CaptureError

READY = ctypes.CFUNCTYPE(None, ctypes.c_uint64, ctypes.c_char_p)
FRAME = ctypes.CFUNCTYPE(
    None,
    ctypes.c_uint64,
    ctypes.c_uint64,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_void_p,
)
FAILURE = ctypes.CFUNCTYPE(None, ctypes.c_uint64, ctypes.c_char_p)
CHANGED = ctypes.CFUNCTYPE(None)


def native_path():
    configured = os.environ.get("SPATIAL_MAC_CAMERA_LIBRARY")
    return (
        Path(configured)
        if configured
        else (
            Path(__file__).resolve().parents[3] / "Validation/Desktop/native/libSpatialCamera.dylib"
        )
    )


def load_native():
    path = native_path()
    if not path.is_file():
        raise CaptureError("UNSUPPORTED_CAPABILITY", "Build the macOS AVFoundation library first")
    native = ctypes.CDLL(str(path))
    native.SWCameraNow.argtypes = []
    native.SWCameraNow.restype = ctypes.c_uint64
    native.SWCameraCatalog.argtypes = []
    native.SWCameraCatalog.restype = ctypes.c_char_p
    native.SWCameraWatch.argtypes = [CHANGED]
    native.SWCameraWatch.restype = ctypes.c_int
    native.SWCameraUnwatch.argtypes = native.SWCameraClose.argtypes = []
    native.SWCameraUnwatch.restype = native.SWCameraClose.restype = None
    native.SWCameraOpen.argtypes = [
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.c_uint64,
        READY,
        FRAME,
        FAILURE,
    ]
    native.SWCameraOpen.restype = ctypes.c_int
    return native


class MacCamera:
    supports_modes = True

    def __init__(self, native=None):
        self._native = native
        self._capture_requested = False
        self._generation = 0
        self._loop = self._ready = None
        self._on_frame = self._on_error = self._changed = None
        self._watching = False
        self._pending = None
        self._scheduled = False
        self._gate = threading.Lock()
        self.supported_modes = []
        # Keep delegates rooted through native stop/drain, including pending TCC.
        self._ready_callback = READY(self._native_ready)
        self._frame_callback = FRAME(self._native_frame)
        self._error_callback = FAILURE(self._native_error)
        self._changed_callback = CHANGED(self._native_changed)

    def _library(self):
        if self._native is None:
            self._native = load_native()
        return self._native

    async def catalog(self):
        raw = await asyncio.to_thread(self._library().SWCameraCatalog)
        if not raw or len(raw) > 65536:
            raise CaptureError("CAPTURE_FAILED", "Camera enumeration failed")
        return [
            dict(
                item,
                present=True,
                backend="macos_avfoundation",
                capabilities={
                    "stable_selection": True,
                    "device_notifications": True,
                    "sharing": "unknown",
                    "external_usage_detection": "conflict_only",
                    "automatic_yield": "conflict_only",
                    "compatibility_status": "unverified",
                    "compatibility_evidence_id": None,
                    "optional_features": [],
                },
            )
            for item in json.loads(raw)
        ]

    def watch(self, changed):
        self._loop = asyncio.get_running_loop()
        self._changed = changed
        if not self._library().SWCameraWatch(self._changed_callback):
            raise CaptureError("CAPTURE_FAILED", "AVFoundation device notifications unavailable")
        self._watching = True

    def _native_changed(self):
        if self._loop and self._changed:
            self._loop.call_soon_threadsafe(self._changed)

    async def stop_watch(self):
        if self._watching:
            await asyncio.to_thread(self._library().SWCameraUnwatch)
            self._watching = False
        self._changed = None

    async def open(self, selected, on_frame, on_error, mode_id=None):
        await self.close()
        self._loop = asyncio.get_running_loop()
        self._ready = self._loop.create_future()
        self._on_frame, self._on_error = on_frame, on_error
        try:
            native = self._library()
            self._capture_requested = True
            if not native.SWCameraOpen(
                selected.encode(),
                mode_id.encode() if mode_id else None,
                self._generation,
                self._ready_callback,
                self._frame_callback,
                self._error_callback,
            ):
                raise CaptureError("CAPTURE_FAILED", "AVFoundation did not accept capture startup")
            mode = await asyncio.wait_for(asyncio.shield(self._ready), 9)
            if mode["mode_id"] != mode_id:
                raise CaptureError(
                    "UNSUPPORTED_CAPABILITY", "Requested capture mode was not applied"
                )
            width, height = mode["width"], mode["height"]
            if width <= 0 or height <= 0 or width * height > 16777216:
                raise CaptureError("CAPTURE_FAILED", "Capture dimensions out of bounds")
            self.supported_modes = mode["modes"]
            return dict(
                mode_id=mode_id,
                source_width_px=width,
                source_height_px=height,
                width_px=width,
                height_px=height,
                fps=mode["fps"],
                pixel_format="RGB8",
                rotation_deg=0,
                crop_rect=[0, 0, width, height],
                mirrored=False,
            )
        except BaseException:
            await self.close()
            raise

    def _native_ready(self, token, raw):
        def deliver():
            if token == self._generation and self._ready and not self._ready.done():
                try:
                    self._ready.set_result(json.loads(raw))
                except (ValueError, TypeError):
                    self._ready.set_exception(
                        CaptureError("CAPTURE_FAILED", "Invalid capture metadata")
                    )

        self._loop.call_soon_threadsafe(deliver)

    def _native_error(self, token, raw):
        code = raw.decode("ascii")

        def deliver():
            if token != self._generation:
                return
            error = CaptureError(code, "AVFoundation could not use the selected camera")
            if self._ready and not self._ready.done():
                self._ready.set_exception(error)
            elif self._on_error:
                self._on_error(error)

        self._loop.call_soon_threadsafe(deliver)

    def _native_frame(self, token, captured, width, height, stride, pointer):
        if token != self._generation:
            return
        try:
            import numpy as np

            if (
                width <= 0
                or height <= 0
                or width * height > 16777216
                or stride < width * 4
                or stride * height > 134217728
            ):
                raise ValueError("Capture dimensions out of bounds")
            raw = ctypes.string_at(pointer, stride * height)
            bgra = np.frombuffer(raw, dtype=np.uint8).reshape(height, stride)[:, : width * 4]
            rgb = bgra.reshape(height, width, 4)[:, :, 2::-1].copy()
            with self._gate:
                self._pending = token, rgb, captured
                if not self._scheduled:
                    self._scheduled = True
                    self._loop.call_soon_threadsafe(self._deliver_frame)
        except (ValueError, OSError):
            self._native_error(token, b"CAPTURE_FAILED")

    def _deliver_frame(self):
        with self._gate:
            pending, self._pending = self._pending, None
            self._scheduled = False
        if pending and pending[0] == self._generation and self._on_frame:
            self._on_frame(pending[1], pending[2])

    async def close(self):
        self._generation += 1
        if self._ready and not self._ready.done():
            self._ready.cancel()
        if self._native is not None and self._capture_requested:
            # Native close stops the session and drains its delegate queue before
            # returning. A canceled permission request cannot later open capture.
            await asyncio.to_thread(self._native.SWCameraClose)
            self._capture_requested = False
        with self._gate:
            self._pending = None
        self._on_frame = self._on_error = None

import pytest

from spatial_tracker import camera
from test_model import valid_intent
from test_session import setup


@pytest.mark.asyncio
async def test_exclusive_status_subscription_requires_initialized_capture(monkeypatch):
    import sys
    from types import SimpleNamespace as NS

    class Capture:
        def __init__(self):
            self.initialized = False
            self.subscribed = False
            self.frame_sources = {}

        def add_failed(self, handler):
            return 1

        async def initialize_with_settings_async(self, settings):
            self.initialized = True

        def add_capture_device_exclusive_control_status_changed(self, handler):
            if not self.initialized:
                raise RuntimeError('MediaCapture must be initialized before subscription')
            self.subscribed = True
            return 2

        def remove_failed(self, token):
            pass

        def remove_capture_device_exclusive_control_status_changed(self, token):
            pass

        def close(self):
            pass

    native = Capture()
    monkeypatch.setitem(sys.modules, 'winrt.windows.media.capture', NS(
        MediaCapture=lambda: native, MediaCaptureInitializationSettings=NS,
        MediaCaptureSharingMode=NS(SHARED_READ_ONLY=1), StreamingCaptureMode=NS(VIDEO=1),
        MediaCaptureMemoryPreference=NS(CPU=1),
        MediaCaptureDeviceExclusiveControlStatus=NS(SHARED_READ_ONLY_AVAILABLE=1)))
    monkeypatch.setitem(sys.modules, 'winrt.windows.media.capture.frames', NS(
        MediaFrameSourceKind=NS(COLOR=1), MediaFrameReaderStartStatus=NS(SUCCESS=1),
        MediaFrameReaderAcquisitionMode=NS(REALTIME=1)))
    monkeypatch.setattr(camera.sys, 'platform', 'win32')
    backend = camera.WindowsCamera()
    async def catalog():
        backend._paths = {'test-camera': 'test-device'}
        return []
    monkeypatch.setattr(backend, 'catalog', catalog)
    try:
        with pytest.raises(camera.CaptureError, match='no color frame source'):
            await backend.open('test-camera', lambda *_: None, lambda *_: None)
        assert native.subscribed
    finally:
        await backend.close()


@pytest.mark.parametrize(
    ("status", "code"),
    [
        ("EXCLUSIVE_CONTROL_NOT_AVAILABLE", "CAMERA_BUSY"),
        ("DEVICE_NOT_AVAILABLE", "CAMERA_UNAVAILABLE"),
        ("OUTPUT_FORMAT_NOT_SUPPORTED", "UNSUPPORTED_CAPABILITY"),
        ("UNKNOWN_FAILURE", "CAPTURE_FAILED"),
    ],
)
def test_reader_start_error_does_not_misreport_every_error_as_camera_busy(status, code):
    assert camera.reader_start_error(status).code == code


@pytest.mark.asyncio
async def test_conflict_during_open_is_not_lost():
    session, hardware, _ = setup()
    original = hardware.open

    async def open_with_conflict(selected, frame, failed):
        mode = await original(selected, frame, failed)
        failed(camera.CaptureError("CAMERA_BUSY", "External exclusive controller"))
        return mode

    hardware.open = open_with_conflict
    await session.apply_intent(valid_intent())
    assert session.state["camera_state"] == "busy"
    assert not hardware.is_open
    await session.close()

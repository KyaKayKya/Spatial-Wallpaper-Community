import asyncio
import ctypes
import json

import numpy as np
import pytest

from spatial_tracker.camera import CaptureError
from spatial_tracker.camera_macos import MacCamera


class Native:
    def __init__(self):
        self.closes = 0
        self.opened = None
        self.changed = None

    def SWCameraClose(self):
        self.closes += 1

    def SWCameraCatalog(self):
        return b'[{"camera_id":"camera","display_name":"Fixture","modes":[]}]'

    def SWCameraOpen(self, camera, mode, token, ready, frame, error):
        self.opened = camera, mode, token, ready, frame, error
        return 1

    def SWCameraWatch(self, changed):
        self.changed = changed
        return 1

    def SWCameraUnwatch(self):
        self.changed = None


async def test_missing_library_fails_explicitly(monkeypatch):
    monkeypatch.setenv("SPATIAL_MAC_CAMERA_LIBRARY", "/nonexistent/spatial-camera.dylib")
    with pytest.raises(CaptureError) as exc:
        await MacCamera().catalog()
    assert exc.value.code == "UNSUPPORTED_CAPABILITY"


async def test_native_permission_or_format_failure_cleans_up():
    for code in ["PERMISSION_DENIED", "UNSUPPORTED_CAPABILITY", "CAMERA_BUSY"]:
        native = Native()
        camera = MacCamera(native)
        opening = asyncio.create_task(camera.open("camera", None, None, "mode"))
        while not native.opened:
            await asyncio.sleep(0)
        _, _, token, _, _, error = native.opened
        error(token, code.encode())
        with pytest.raises(CaptureError) as exc:
            await opening
        assert exc.value.code == code
        assert native.closes == 1


async def test_frame_keeps_capture_time_rgb_and_stride():
    native = Native()
    camera = MacCamera(native)
    received = asyncio.get_running_loop().create_future()
    opening = asyncio.create_task(
        camera.open("camera", lambda rgb, stamp: received.set_result((rgb, stamp)), None, "mode")
    )
    while not native.opened:
        await asyncio.sleep(0)
    _, _, token, ready, frame, _ = native.opened
    ready(token, json.dumps(dict(width=2, height=1, fps=30, mode_id="mode", modes=[])).encode())
    mode = await opening
    assert mode["mode_id"] == "mode"
    # Two BGRA pixels plus driver row padding; not treated as a third pixel.
    data = ctypes.create_string_buffer(bytes([3, 2, 1, 255, 6, 5, 4, 255, 9, 9, 9, 9]))
    frame(token, 123456, 2, 1, 12, ctypes.addressof(data))
    rgb, stamp = await asyncio.wait_for(received, 1)
    assert stamp == 123456
    assert np.array_equal(rgb, [[[1, 2, 3], [4, 5, 6]]])
    await camera.close()


async def test_cancel_drops_late_permission_and_frame_callbacks():
    native = Native()
    camera = MacCamera(native)
    received = []
    opening = asyncio.create_task(camera.open("camera", lambda *args: received.append(args), None))
    while not native.opened:
        await asyncio.sleep(0)
    _, _, token, ready, frame, error = native.opened
    opening.cancel()
    with pytest.raises(asyncio.CancelledError):
        await opening
    ready(token, b"{}")
    frame(token, 1, 1, 1, 4, 0)
    error(token, b"CAPTURE_FAILED")
    await asyncio.sleep(0)
    assert native.closes == 1
    assert not received


async def test_catalog_and_notifications_do_not_open_capture():
    native = Native()
    camera = MacCamera(native)
    assert (await camera.catalog())[0]["capabilities"]["sharing"] == "unknown"
    changed = asyncio.Event()
    camera.watch(changed.set)
    native.changed()
    await asyncio.wait_for(changed.wait(), 1)
    await camera.stop_watch()
    assert not native.opened and native.changed is None

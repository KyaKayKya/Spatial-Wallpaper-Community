import sys

import pytest

from spatial_tracker import activity, camera


@pytest.mark.skipif(sys.platform != "win32", reason="Windows native adapter")
@pytest.mark.asyncio
async def test_native_catalog_and_watch_can_start_without_opening_camera():
    backend = camera.WindowsCamera()
    devices = await backend.catalog()
    assert all(item["camera_id"].startswith("winrt-") for item in devices)
    backend.watch(lambda: None)
    await backend.stop_watch()
    await backend.close()


@pytest.mark.skipif(sys.platform != "win32", reason="Windows native adapter")
def test_native_sensor_monitor_start_stop_is_idempotent():
    monitor = activity.SensorActivityMonitor(lambda *_: None)
    monitor.start()
    monitor.close()
    monitor.close()


@pytest.mark.skipif(sys.platform != "win32", reason="Windows bitmap conversion")
def test_native_bitmap_conversion_keeps_rgb_channels():
    import numpy as np
    from winrt.windows.graphics.imaging import SoftwareBitmap, BitmapPixelFormat, BitmapAlphaMode
    from winrt.windows.storage.streams import Buffer

    buf = Buffer(8)
    buf.length = 8
    memoryview(buf)[:] = bytes([10, 20, 30, 255, 40, 50, 60, 255])
    bitmap = SoftwareBitmap.create_copy_with_alpha_from_buffer(
        buf, BitmapPixelFormat.RGBA8, 2, 1, BitmapAlphaMode.IGNORE
    )
    try:
        assert np.array_equal(
            camera.bitmap_to_rgb(bitmap), np.array([[[10, 20, 30], [40, 50, 60]]], dtype=np.uint8)
        )
    finally:
        bitmap.close()

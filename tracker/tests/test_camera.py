from spatial_tracker import camera


def test_camera_id_uses_device_path_not_name_or_order():
    assert camera.camera_id("PATH-A") == camera.camera_id("path-a")
    assert camera.camera_id("PATH-A") != camera.camera_id("PATH-B")

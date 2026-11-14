import asyncio

import pytest

from spatial_tracker import session
from test_model import valid_intent


class Camera:
    def __init__(self):
        self.devices = [{"camera_id": "camera-A"}, {"camera_id": "camera-B"}]
        self.is_open = False
        self.opened = []
        self.wait_open = None
        self.close_failure = False

    async def catalog(self):
        return self.devices.copy()

    async def open(self, camera_id, on_frame, on_error):
        self.opened.append(camera_id)
        if self.wait_open:
            await self.wait_open.wait()
        self.is_open = True
        self.on_frame = on_frame
        return {"width_px": 640, "height_px": 480, "fps": 30}

    async def close(self):
        if self.close_failure:
            raise TimeoutError("release failed")
        self.is_open = False


def setup():
    camera = Camera()
    events = []
    worker = session.Session(
        camera,
        lambda state: events.append(("state", state.copy())),
        lambda *args: events.append(("stream", args)),
        lambda *args: events.append(("frame", args)),
    )
    return worker, camera, events


@pytest.mark.asyncio
async def test_external_use_releases_then_resumes_same_camera():
    worker, camera, events = setup()
    await worker.apply_intent(valid_intent())
    assert worker.state["camera_state"] == "streaming"
    await worker.external_activity("camera-A", "active")
    assert worker.state["camera_state"] == "yielded"
    assert not camera.is_open
    await worker.external_activity("camera-A", "inactive")
    assert camera.opened == ["camera-A", "camera-A"]
    assert worker.state["camera_state"] == "streaming"
    await worker.close()


@pytest.mark.asyncio
async def test_no_auto_resume_when_paused_or_not_allowed():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent() | {"auto_resume_allowed": False})
    await worker.external_activity("camera-A", "active")
    await worker.external_activity("camera-A", "inactive")
    assert camera.opened == ["camera-A"]
    await worker.apply_intent(
        valid_intent()
        | {
            "intent_revision": "2",
            "capture_requested": False,
        }
    )
    await worker.external_activity("camera-A", "inactive")
    assert not camera.is_open
    assert worker.state["camera_state"] == "closed"
    await worker.close()


@pytest.mark.asyncio
async def test_unknown_external_activity_does_not_resume():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    await worker.external_activity("camera-A", "active")
    await worker.external_activity("camera-A", "unknown")
    assert camera.opened == ["camera-A"]
    await worker.close()


@pytest.mark.asyncio
async def test_other_camera_activity_does_not_release_selected_camera():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    await worker.external_activity("camera-B", "active")
    assert camera.is_open
    assert camera.opened == ["camera-A"]
    await worker.close()


@pytest.mark.asyncio
async def test_unplug_never_opens_another_camera():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    camera.devices = [{"camera_id": "camera-B"}]
    await worker.refresh()
    assert worker.state["camera_state"] == "unavailable"
    assert camera.opened == ["camera-A"]
    camera.devices.append({"camera_id": "camera-A"})
    await worker.refresh()
    assert camera.opened == ["camera-A", "camera-A"]
    await worker.close()


@pytest.mark.asyncio
async def test_pause_while_opening_closes_late_open():
    worker, camera, _ = setup()
    camera.wait_open = asyncio.Event()
    opening = asyncio.create_task(worker.apply_intent(valid_intent()))
    await asyncio.sleep(0)
    pause = asyncio.create_task(
        worker.apply_intent(
            valid_intent()
            | {
                "intent_revision": "2",
                "capture_requested": False,
            }
        )
    )
    await asyncio.sleep(0)
    camera.wait_open.set()
    await asyncio.gather(opening, pause)
    assert not camera.is_open
    assert worker.state["camera_state"] == "closed"
    await worker.close()


@pytest.mark.asyncio
async def test_old_stream_callback_cannot_publish_after_switch():
    worker, camera, events = setup()
    await worker.apply_intent(valid_intent())
    old_callback = camera.on_frame
    await worker.apply_intent(
        valid_intent()
        | {
            "intent_revision": "2",
            "camera_id": "camera-B",
        }
    )
    old_callback("old-frame", 100)
    camera.on_frame("new-frame", 101)
    frames = [value for kind, value in events if kind == "frame"]
    assert len(frames) == 1 and frames[0][-1] == "new-frame"
    await worker.close()


@pytest.mark.asyncio
async def test_failed_release_never_reports_yielded():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    camera.close_failure = True
    await worker.external_activity("camera-A", "active")
    assert worker.state["camera_state"] == "error"
    assert worker.state["last_error"]["code"] == "RELEASE_TIMEOUT"
    camera.close_failure = False
    await worker.close()

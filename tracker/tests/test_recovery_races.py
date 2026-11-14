import asyncio

import pytest

from test_model import valid_intent
from test_session import setup
from test_tcp_boundaries import connected


async def wait_until(predicate):
    async with asyncio.timeout(2):
        while not predicate():
            await asyncio.sleep(0.001)


@pytest.mark.asyncio
@pytest.mark.parametrize("latest", ["unknown", "active"])
async def test_worker_latest_activity_blocks_pending_auto_resume(latest):
    async with connected() as (peer, camera, worker, task):
        request = await peer.send("SetTrackerIntent", valid_intent())
        await peer.result(request)
        worker._activity("camera-A", "active")
        await wait_until(lambda: worker.session.state["camera_state"] == "yielded")
        worker._activity("camera-A", "inactive")
        await asyncio.sleep(0.03)
        worker._activity("camera-A", latest)
        await asyncio.sleep(0.25)
        assert camera.opened == ["camera-A"]
        assert not camera.is_open
        assert worker.session.state["external_usage"] == latest
        assert worker.session.state["camera_state"] == "yielded"


@pytest.mark.asyncio
@pytest.mark.parametrize("latest", ["unknown", "active"])
async def test_worker_rechecks_activity_after_recovery_open_completes(latest):
    async with connected() as (peer, camera, worker, task):
        request = await peer.send("SetTrackerIntent", valid_intent())
        await peer.result(request)
        worker._activity("camera-A", "active")
        await wait_until(lambda: worker.session.state["camera_state"] == "yielded")
        camera.wait_open = asyncio.Event()
        worker._activity("camera-A", "inactive")
        await wait_until(lambda: len(camera.opened) == 2)
        worker._activity("camera-A", latest)
        camera.wait_open.set()
        await wait_until(lambda: not worker._activity_tasks)
        assert not camera.is_open
        assert worker.session.stream_id is None
        assert worker.session.state["camera_state"] == "yielded"


@pytest.mark.asyncio
async def test_rejected_manual_retry_cannot_authorize_future_automatic_resume():
    session, camera, _ = setup()
    intent = valid_intent() | {"auto_resume_allowed": False}
    await session.apply_intent(intent)
    await session.external_activity("camera-A", "active")
    await session.apply_intent(intent | {"intent_revision": "2", "retry_generation": "1"})
    assert session.state["camera_state"] == "yielded"
    await session.external_activity("camera-A", "inactive")
    assert camera.opened == ["camera-A"]
    assert not camera.is_open
    # A subsequent explicit retry in the inactive state still works once.
    await session.apply_intent(intent | {"intent_revision": "3", "retry_generation": "2"})
    assert camera.opened == ["camera-A", "camera-A"]
    await session.close()

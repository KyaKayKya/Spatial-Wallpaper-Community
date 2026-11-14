import asyncio
from contextlib import asynccontextmanager
import uuid

import numpy as np
import pytest

from spatial_tracker.clock import CLOCK_DOMAIN, qpc_us
from spatial_tracker.protocol import ProtocolError, encode_message, read_message
from spatial_tracker.worker import TrackerWorker
from test_model import valid_intent
from test_worker import Estimator, Hardware, bootstrap


class Peer:
    def __init__(self, reader, writer, config):
        self.reader, self.writer, self.config = reader, writer, config
        self.sequence = 0
        self.connection = str(uuid.uuid4())

    async def send(self, name, payload, kind="command", request=None):
        self.sequence += 1
        request = request or (str(uuid.uuid4()) if kind == "command" else None)
        self.writer.write(
            encode_message(
                {
                    "protocol_version": 1,
                    "kind": kind,
                    "type": name,
                    "runtime_instance_id": self.config["runtime_instance_id"],
                    "sender_instance_id": self.config["runtime_instance_id"],
                    "connection_id": self.connection,
                    "seq": str(self.sequence),
                    "request_id": request,
                    "sent_at_us": str(qpc_us()),
                    "state_revision": None,
                    "payload": payload,
                }
            )
        )
        await self.writer.drain()
        return request

    async def until(self, predicate):
        messages = []
        while True:
            item = await asyncio.wait_for(read_message(self.reader), 3)
            messages.append(item)
            if predicate(item):
                return messages

    async def result(self, request):
        messages = await self.until(
            lambda m: m["request_id"] == request and m["payload"]["status"] != "accepted"
        )
        return messages[-1]["payload"]


@asynccontextmanager
async def connected(estimator=Estimator, optional_features=None):
    accepted = asyncio.Queue()

    async def accept(reader, writer):
        await accepted.put((reader, writer))

    server = await asyncio.start_server(accept, "127.0.0.1", 0)
    config = bootstrap(server.sockets[0].getsockname()[1])
    camera = Hardware()
    worker = TrackerWorker(camera, estimator, monitor_factory=None)
    task = asyncio.create_task(worker.run(config))
    reader, writer = await asyncio.wait_for(accepted.get(), 2)
    peer = Peer(reader, writer, config)
    await read_message(reader)
    await peer.send(
        "Welcome",
        {
            "protocol_version": 1,
            "connection_id": peer.connection,
            "heartbeat_interval_ms": 2000,
            "clock_domain": CLOCK_DOMAIN,
            **({"optional_features": optional_features} if optional_features is not None else {}),
        },
        "handshake",
    )
    try:
        yield peer, camera, worker, task
    finally:
        writer.close()
        await writer.wait_closed()
        if not task.done():
            task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        server.close()
        await server.wait_closed()


@pytest.mark.asyncio
async def test_real_tcp_stream_descriptor_precedes_first_sample():
    class Inference(Estimator):
        def submit(self, stream, frame, stamp, rgb):
            self.callback(
                {
                    "stream_id": stream,
                    "frame_id": str(frame),
                    "captured_at_us": str(stamp),
                    "processed_at_us": str(qpc_us()),
                    "tracking_state": "searching",
                    "head_pose": None,
                    "eye_position": None,
                    "validity": {"head_pose": False, "eye_position": False},
                    "quality": {"flags": ["no_face"], "reprojection_error_px": None},
                    "extensions": {},
                }
            )

    async with connected(Inference) as (peer, camera, instance, task):
        await peer.send("SetTrackerIntent", valid_intent())
        while instance.session.stream_id is None:
            await asyncio.sleep(0.001)
        camera.on_frame(np.zeros((480, 640, 3), dtype=np.uint8), qpc_us())
        messages = await peer.until(lambda m: m["type"] == "TrackingSample")
        types = [m["type"] for m in messages]
        assert types.index("TrackerStreamChanged") < types.index("TrackingSample")
        descriptor = next(
            m["payload"]["stream_descriptor"]
            for m in messages
            if m["type"] == "TrackerStreamChanged"
        )
        assert descriptor["stream_id"] == messages[-1]["payload"]["stream_id"]
        assert [int(m["seq"]) for m in messages] == list(range(2, len(messages) + 2))


@pytest.mark.asyncio
async def test_invalid_sequence_disconnects_and_releases():
    async with connected() as (peer, camera, instance, task):
        request = await peer.send("SetTrackerIntent", valid_intent())
        await peer.result(request)
        peer.sequence -= 1
        await peer.send("GetState", {"scopes": ["tracking"]})
        with pytest.raises(ProtocolError, match="sequence"):
            await asyncio.wait_for(task, 3)
        assert not camera.is_open


@pytest.mark.asyncio
async def test_opening_command_is_superseded_by_pause():
    async with connected() as (peer, camera, instance, task):
        camera.wait_open = asyncio.Event()
        opening = await peer.send("SetTrackerIntent", valid_intent())
        while not camera.opened:
            await asyncio.sleep(0.001)
        pause = await peer.send(
            "SetTrackerIntent",
            valid_intent() | {"intent_revision": "2", "capture_requested": False},
        )
        while instance.session.intent["intent_revision"] != "2":
            await asyncio.sleep(0.001)
        camera.wait_open.set()
        results = {}
        while len(results) < 2:
            messages = await peer.until(
                lambda m: m["kind"] == "response" and m["payload"]["status"] != "accepted"
            )
            message = messages[-1]
            results[message["request_id"]] = message["payload"]
        assert results[opening]["error"]["code"] == "SUPERSEDED"
        assert results[pause]["status"] == "completed"
        assert not camera.is_open


@pytest.mark.asyncio
async def test_unknown_command_and_state_snapshot():
    async with connected() as (peer, camera, instance, task):
        request = await peer.send("Unexpected", {})
        assert (await peer.result(request))["error"]["code"] == "UNSUPPORTED_MESSAGE"
        request = await peer.send("GetState", {"scopes": ["cameras", "tracking"]})
        state = (await peer.result(request))["result"]
        assert state["tracking"]["camera_state"] == "closed"
        assert state["camera_catalog"]["catalog_ready"]
        assert state["stream_descriptor"] is None
        assert not camera.opened


@pytest.mark.asyncio
async def test_silent_runtime_times_out_and_releases():
    async with connected() as (peer, camera, instance, task):
        request = await peer.send("SetTrackerIntent", valid_intent())
        await peer.result(request)
        with pytest.raises(TimeoutError):
            await asyncio.wait_for(asyncio.shield(task), 7)
        assert not camera.is_open


@pytest.mark.asyncio
async def test_owner_disconnect_stops_watch_then_drains_catalog_without_cancel():
    async with connected() as (peer, camera, instance, task):
        request = await peer.send("GetState", {"scopes": ["cameras"]})
        await peer.result(request)
        entered, finish, stopped = asyncio.Event(), asyncio.Event(), asyncio.Event()
        completed = False

        async def catalog():
            nonlocal completed
            entered.set()
            await finish.wait()
            completed = True
            return []

        async def stop_watch():
            stopped.set()

        camera.catalog, camera.stop_watch = catalog, stop_watch
        camera.changed()
        await asyncio.wait_for(entered.wait(), 2)
        peer.writer.close()
        await peer.writer.wait_closed()
        await asyncio.wait_for(stopped.wait(), 2)
        await asyncio.sleep(0)
        assert not task.done()
        assert not instance._catalog_task.cancelled()
        finish.set()
        await asyncio.wait_for(task, 2)
        assert completed

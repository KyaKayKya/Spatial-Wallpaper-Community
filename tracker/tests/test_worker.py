import asyncio
import uuid

import pytest

from spatial_tracker.clock import CLOCK_DOMAIN

from spatial_tracker import worker
from spatial_tracker.protocol import encode_message, read_message
from test_model import valid_intent
from test_session import Camera


class Hardware(Camera):
    def watch(self, callback):
        self.changed = callback

    async def stop_watch(self):
        pass


class Estimator:
    def __init__(self, callback):
        self.callback = callback

    def set_stream(self, stream):
        self.stream = stream

    def set_preview(self, callback):
        self.preview = callback

    def close(self):
        pass


def bootstrap(port):
    return {
        "host": "127.0.0.1",
        "port": port,
        "token": "local-test-token",
        "runtime_instance_id": str(uuid.uuid4()),
    }


@pytest.mark.parametrize("patch", [{"host": "example.com"}, {"port": 0}, {"token": ""}])
def test_bootstrap_rejects_remote_or_invalid_connection(patch):
    with pytest.raises(ValueError):
        worker.validate_bootstrap(bootstrap(12345) | patch)


@pytest.mark.asyncio
@pytest.mark.parametrize('preview_allowed', [False, True])
async def test_real_tcp_handshake_intent_replay_and_disconnect_release(preview_allowed):
    connections = asyncio.Queue()

    async def accept(reader, writer):
        await connections.put((reader, writer))

    server = await asyncio.start_server(accept, "127.0.0.1", 0)
    config = bootstrap(server.sockets[0].getsockname()[1])
    hardware = Hardware()
    instance = worker.TrackerWorker(hardware, Estimator, monitor_factory=None)
    task = asyncio.create_task(instance.run(config))
    reader, writer = await asyncio.wait_for(connections.get(), 2)
    hello = await read_message(reader)
    assert hello["type"] == "Hello" and hello["payload"]["role"] == "tracker"
    assert hello["payload"]["token"] == "local-test-token"
    connection = str(uuid.uuid4())
    sequence = 0

    async def send(kind, name, payload, request=None):
        nonlocal sequence
        sequence += 1
        writer.write(
            encode_message(
                {
                    "protocol_version": 1,
                    "kind": kind,
                    "type": name,
                    "runtime_instance_id": config["runtime_instance_id"],
                    "sender_instance_id": config["runtime_instance_id"],
                    "connection_id": connection,
                    "seq": str(sequence),
                    "request_id": request,
                    "sent_at_us": "100",
                    "state_revision": None,
                    "payload": payload,
                }
            )
        )
        await writer.drain()

    async def result(request):
        while True:
            message = await asyncio.wait_for(read_message(reader), 2)
            if message["request_id"] == request and message["payload"]["status"] != "accepted":
                return message

    try:
        await send(
            "handshake",
            "Welcome",
            {
                "protocol_version": 1,
                "connection_id": connection,
                "heartbeat_interval_ms": 2000,
                "clock_domain": CLOCK_DOMAIN,
                "capabilities": ['camera_preview_v1'] if preview_allowed else [],
            },
        )
        request = str(uuid.uuid4())
        await send("command", "SetTrackerIntent", valid_intent(), request)
        response = await result(request)
        assert response["payload"]["status"] == "completed"
        assert hardware.is_open
        preview_request = str(uuid.uuid4())
        await send('command', 'SetCameraPreview', {'enabled': True}, preview_request)
        preview_response = await result(preview_request)
        assert preview_response['payload']['status'] == ('completed' if preview_allowed else 'failed')
        if preview_allowed:
            assert instance._estimator.preview is not None
            instance._preview({'stream_id': instance.session.stream_id, 'frame_id': '1'})
            while True:
                image = await asyncio.wait_for(read_message(reader), 2)
                if image['type'] == 'CameraPreview':
                    break
            assert image['kind'] == 'preview' and image['payload']['frame_id'] == '1'
            preview_request = str(uuid.uuid4())
            await send('command', 'SetCameraPreview', {'enabled': False}, preview_request)
            assert (await result(preview_request))['payload']['status'] == 'completed'
            assert instance._estimator.preview is None and instance._outbox._preview is None
        await send("command", "SetTrackerIntent", valid_intent(), request)
        assert (await result(request))["payload"]["status"] == "completed"
        assert hardware.opened == ["camera-A"]
        await send(
            "command", "SetTrackerIntent", valid_intent() | {"camera_id": "camera-B"}, request
        )
        assert (await result(request))["payload"]["error"]["code"] == "REQUEST_ID_REUSED"
        assert hardware.opened == ["camera-A"]
        writer.close()
        await writer.wait_closed()
        await asyncio.wait_for(task, 3)
        assert not hardware.is_open
    finally:
        writer.close()
        if not task.done():
            task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        server.close()
        await server.wait_closed()


@pytest.mark.asyncio
async def test_runtime_identity_mismatch_closes_without_capture():
    async def accept(reader, writer):
        await read_message(reader)
        writer.write(
            encode_message(
                {
                    "protocol_version": 1,
                    "kind": "handshake",
                    "type": "Welcome",
                    "runtime_instance_id": str(uuid.uuid4()),
                    "sender_instance_id": str(uuid.uuid4()),
                    "connection_id": str(uuid.uuid4()),
                    "seq": "1",
                    "request_id": None,
                    "sent_at_us": "1",
                    "state_revision": None,
                    "payload": {},
                }
            )
        )
        await writer.drain()
        writer.close()

    server = await asyncio.start_server(accept, "127.0.0.1", 0)
    hardware = Hardware()
    instance = worker.TrackerWorker(hardware, Estimator, monitor_factory=None)
    try:
        with pytest.raises(ValueError):
            await instance.run(bootstrap(server.sockets[0].getsockname()[1]))
        assert not hardware.is_open
    finally:
        server.close()
        await server.wait_closed()


@pytest.mark.asyncio
async def test_write_loop_observes_shutdown_after_completed_drain(monkeypatch):
    instance = worker.TrackerWorker(Hardware(), Estimator, monitor_factory=None)
    instance.config = bootstrap(12345)
    draining = asyncio.Event()
    original_wait_for = asyncio.wait_for

    class Writer:
        def write(self, data):
            pass

        async def drain(self):
            draining.set()
            await asyncio.Future()

    async def completed_drain_after_cancel(awaitable, timeout):
        # Reproduce the observed boundary: cancellation was requested, but the
        # drain await completed and the writer resumed during owner shutdown.
        try:
            return await awaitable
        except asyncio.CancelledError:
            return None

    monkeypatch.setattr(worker.asyncio, "wait_for", completed_drain_after_cancel)
    instance._outbox.put_control(
        {"kind": "heartbeat", "type": "Ping", "request_id": None, "payload": {"nonce": "1"}}
    )
    task = asyncio.create_task(instance._write(Writer()))
    try:
        await original_wait_for(draining.wait(), 1)
        instance._closing = True
        task.cancel()
        await original_wait_for(asyncio.shield(task), .2)
        assert task.done() and not task.cancelled()
    finally:
        if not task.done():
            task.cancel()
        await asyncio.gather(task, return_exceptions=True)

import asyncio
import json
from pathlib import Path
import sys

import pytest

from spatial_tracker.clock import CLOCK_DOMAIN

from spatial_tracker.protocol import read_message
from test_tcp_boundaries import Peer
from test_worker import bootstrap


@pytest.mark.asyncio
@pytest.mark.parametrize("attempt", range(3))
async def test_actual_worker_process_bootstrap_state_and_owner_disconnect(attempt):
    if not Path(".cache/models/face_landmarker.task").exists():
        pytest.skip("Download model assets before the full native process test")
    accepted = asyncio.Queue()

    async def accept(reader, writer):
        await accepted.put((reader, writer))

    server = await asyncio.start_server(accept, "127.0.0.1", 0)
    config = bootstrap(server.sockets[0].getsockname()[1])
    child = await asyncio.create_subprocess_exec(
        sys.executable,
        "-X",
        "faulthandler",
        "-m",
        "spatial_tracker",
        "run",
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    writer = None
    try:
        child.stdin.write((json.dumps(config) + "\n").encode())
        await child.stdin.drain()
        child.stdin.close()
        reader, writer = await asyncio.wait_for(accepted.get(), 10)
        hello = await read_message(reader)
        assert hello["payload"]["token"] == config["token"]
        peer = Peer(reader, writer, config)
        await peer.send(
            "Welcome",
            {
                "protocol_version": 1,
                "connection_id": peer.connection,
                "heartbeat_interval_ms": 2000,
                "clock_domain": CLOCK_DOMAIN,
            },
            "handshake",
        )
        request = await peer.send("GetState", {"scopes": ["cameras", "tracking"]})
        response = await peer.result(request)
        assert response["status"] == "completed"
        assert response["result"]["camera_catalog"]["catalog_ready"]
        assert response["result"]["tracking"]["camera_state"] == "closed"
        writer.close()
        await writer.wait_closed()
        stdout, stderr = await asyncio.wait_for(child.communicate(), 5)
        assert child.returncode == 0, stderr.decode(errors="replace")
        assert config["token"].encode() not in stdout + stderr
    finally:
        if writer:
            writer.close()
        if child.returncode is None:
            child.kill()
            await child.wait()
        server.close()
        await server.wait_closed()

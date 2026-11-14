import asyncio
import json
import os
from pathlib import Path
import sys
import uuid

import pytest

from spatial_tracker.protocol import encode_message, read_message
from spatial_tracker.clock import CLOCK_DOMAIN, qpc_us


def test_fixed_simulation_keeps_eye_and_binocular_reference_while_frames_advance():
    from spatial_tracker.simulation import MeasurementReplay
    samples = []
    replay = MeasurementReplay(samples.append, scenario="fixed")
    replay.set_stream(str(uuid.uuid4()))
    for frame, elapsed in enumerate([0, 1, 30], start=1):
        captured = replay.start + elapsed * 1_000_000
        replay.submit(replay.stream, frame, captured, None)
        sample = samples[-1]
        assert sample["eye_position"] == [0, .03, .635]
        assert sample["extensions"]["binocular_center_v1"] == {"center_uv": [0, 0], "ipd_px": 100}
        assert sample["frame_id"] == str(frame)
        assert sample["captured_at_us"] == str(captured)


@pytest.mark.asyncio
@pytest.mark.parametrize("prefix", [b"", b"\xef\xbb\xbf"])
@pytest.mark.parametrize("recorded", [False, True, "fixed"])
async def test_simulation_uses_real_worker_handshake_intent_and_transport(prefix, recorded, tmp_path):
    incoming = asyncio.Queue()

    async def accept(reader, writer):
        await incoming.put((reader, writer))

    server = await asyncio.start_server(accept, "127.0.0.1", 0)
    runtime = str(uuid.uuid4())
    config = dict(host="127.0.0.1", port=server.sockets[0].getsockname()[1],
                  token=uuid.uuid4().hex, runtime_instance_id=runtime)
    recording_args = []
    if recorded is True:
        path = tmp_path / "recording.json"
        path.write_text(json.dumps(dict(
            descriptor=dict(actual_mode=dict(mode_id=None, source_width_px=1280,
                source_height_px=720, width_px=1280, height_px=720, fps=30,
                pixel_format="RGB8", rotation_deg=0, crop_rect=[0, 0, 1280, 720],
                mirrored=False)),
            samples=[dict(offset_us=0, sample=dict(
                stream_id="replaced", frame_id="1", captured_at_us="0", processed_at_us="0",
                tracking_state="tracking", validity=dict(head_pose=False, eye_position=True),
                head_pose=None, eye_position=[0, .03, .635],
                quality=dict(flags=[], reprojection_error_px=None), extensions={}))])),
            encoding="utf-8")
        recording_args = ["--recording", str(path)]
    elif recorded == "fixed":
        recording_args = ["--scenario", "fixed"]
    child = await asyncio.create_subprocess_exec(
        sys.executable, "-m", "spatial_tracker", "simulate", *recording_args,
        stdin=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        env={**os.environ, "PYTHONPATH": str(Path("src").resolve())})
    writer = None
    try:
        child.stdin.write(prefix + (json.dumps(config) + "\n").encode())
        await child.stdin.drain()
        child.stdin.close()
        try:
            reader, writer = await asyncio.wait_for(incoming.get(), 3)
        except TimeoutError:
            assert child.returncode is None, "Simulator exited before the real TCP handshake"
            pytest.fail("Simulator did not connect")
        hello = await read_message(reader)
        assert hello["type"] == "Hello"
        assert hello["payload"]["token"] == config["token"]
        connection = str(uuid.uuid4())
        seq = 0

        async def send(name, payload, kind="command"):
            nonlocal seq
            seq += 1
            writer.write(encode_message(dict(
                protocol_version=1, runtime_instance_id=runtime, sender_instance_id=runtime,
                connection_id=connection, seq=str(seq), sent_at_us=str(qpc_us()),
                state_revision=None, kind=kind, type=name,
                request_id=str(uuid.uuid4()) if kind == "command" else None, payload=payload)))
            await writer.drain()

        await send("Welcome", dict(protocol_version=1, connection_id=connection,
                   heartbeat_interval_ms=2000, clock_domain=CLOCK_DOMAIN,
                   optional_features=["binocular_center_v1"] if recorded == "fixed" else []), "handshake")
        await send("SetTrackerIntent", dict(intent_revision="1", camera_id="simulation",
                   capture_requested=True, auto_resume_allowed=True,
                   capture_preference=dict(mode_id=None, max_inference_hz=60),
                   required_features=["eye_pose"], retry_generation="0"))
        descriptor = None
        for _ in range(30):
            message = await asyncio.wait_for(read_message(reader), 3)
            if message["type"] == "TrackerStreamChanged":
                descriptor = message["payload"]["stream_descriptor"]
            if message["type"] == "TrackingSample":
                sample = message["payload"]
                assert descriptor and descriptor["stream_id"] == sample["stream_id"]
                assert descriptor["coordinate_frame"] == "camera_upright_v1"
                assert descriptor["length_unit"] == "model_m"
                assert descriptor["actual_mode"]["width_px"] == (1280 if recorded is True else 640)
                assert sample["validity"]["eye_position"]
                assert sample["eye_position"][2] > 0
                if recorded == "fixed":
                    assert sample["eye_position"] == [0, .03, .635]
                    assert sample["extensions"]["binocular_center_v1"] == {"center_uv": [0, 0], "ipd_px": 100}
                assert 0 <= qpc_us() - int(sample["captured_at_us"]) < 200_000
                break
        else:
            pytest.fail("No sample after SetTrackerIntent")
        writer.close()
        await writer.wait_closed()
        await asyncio.wait_for(child.wait(), 5)
        assert child.returncode == 0
        assert config["token"].encode() not in await child.stderr.read()
    finally:
        if writer:
            writer.close()
        if child.returncode is None:
            child.kill()
            await child.wait()
        server.close()
        await server.wait_closed()

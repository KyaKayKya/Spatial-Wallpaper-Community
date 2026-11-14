import asyncio
import struct

import pytest

from spatial_tracker import protocol


@pytest.mark.asyncio
async def test_split_and_coalesced_frames():
    reader = asyncio.StreamReader()
    reader.feed_data(b"\x00\x00")
    reader.feed_data(b'\x00\x07{"x":1}\x00\x00\x00\x07{"x":2}')
    assert await protocol.read_message(reader) == {"x": 1}
    assert await protocol.read_message(reader) == {"x": 2}


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "raw",
    [
        b'{"a":1,"a":2}',
        b'{"x":NaN}',
        b'{"x":1e999}',
        b"[]",
        b'{"x":"\xff"}',
    ],
)
async def test_rejects_invalid_json(raw):
    reader = asyncio.StreamReader()
    reader.feed_data(struct.pack(">I", len(raw)) + raw)
    with pytest.raises(protocol.ProtocolError):
        await protocol.read_message(reader)


@pytest.mark.asyncio
@pytest.mark.parametrize("length", [0, 65537, 0xFFFFFFFF])
async def test_rejects_length_before_waiting_for_payload(length):
    reader = asyncio.StreamReader()
    reader.feed_data(struct.pack(">I", length))
    with pytest.raises(protocol.ProtocolError):
        await asyncio.wait_for(protocol.read_message(reader), 0.1)


def test_encode_non_ascii_and_reject_nan():
    encoded = protocol.encode_message({"name": "相机"})
    assert encoded[4:].decode() == '{"name":"相机"}'
    with pytest.raises(protocol.ProtocolError):
        protocol.encode_message({"x": float("nan")})


@pytest.mark.asyncio
async def test_control_fifo_and_latest_sample_only():
    box = protocol.Outbox(max_controls=2)
    box.put_sample({"frame": 1})
    box.put_sample({"frame": 2})
    box.put_control({"state": "opening"})
    box.put_control({"state": "streaming"})
    with pytest.raises(protocol.QueueFull):
        box.put_control({"state": "error"})
    assert await box.get() == {"state": "opening"}
    assert await box.get() == {"state": "streaming"}
    assert await box.get() == {"frame": 2}


@pytest.mark.asyncio
async def test_new_stream_clears_pending_old_sample():
    box = protocol.Outbox()
    box.put_sample({"stream": "old"})
    box.clear_sample()
    box.put_control({"stream": "new"})
    assert await box.get() == {"stream": "new"}
    with pytest.raises(asyncio.TimeoutError):
        await asyncio.wait_for(box.get(), 0.01)

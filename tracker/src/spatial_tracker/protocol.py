"""Length-prefixed JSON and bounded outgoing queues, independent of camera APIs."""

import asyncio
from collections import deque
import copy
import json
import math
import struct

MAX_FRAME = 65_536


class ProtocolError(ValueError):
    pass


class QueueFull(RuntimeError):
    pass


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ProtocolError("Duplicate JSON key")
        result[key] = value
    return result


def _validate(value, depth=0):
    if depth > 16:
        raise ProtocolError("JSON nesting exceeds limit")
    if isinstance(value, float) and not math.isfinite(value):
        raise ProtocolError("Non-finite number")
    if isinstance(value, dict):
        for key, item in value.items():
            if not isinstance(key, str):
                raise ProtocolError("Object keys must be strings")
            _validate(item, depth + 1)
    elif isinstance(value, list):
        for item in value:
            _validate(item, depth + 1)


def encode_message(message):
    if not isinstance(message, dict):
        raise ProtocolError("Message must be an object")
    try:
        _validate(message)
        raw = json.dumps(
            message, ensure_ascii=False, allow_nan=False, separators=(",", ":")
        ).encode("utf-8")
    except (ValueError, TypeError, RecursionError, UnicodeError) as exc:
        raise ProtocolError("Invalid JSON message") from exc
    if not 0 < len(raw) <= MAX_FRAME:
        raise ProtocolError("Frame length out of range")
    return struct.pack(">I", len(raw)) + raw


async def read_message(reader):
    length = struct.unpack(">I", await reader.readexactly(4))[0]
    if not 0 < length <= MAX_FRAME:
        raise ProtocolError("Frame length out of range")
    raw = await reader.readexactly(length)
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=_pairs)
        if not isinstance(value, dict):
            raise ProtocolError("Message must be an object")
        _validate(value)
        return value
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise ProtocolError("Invalid JSON frame") from exc


class Outbox:
    """One event-loop owner; controls are FIFO and measurements replace one slot."""

    def __init__(self, max_controls=64, max_control_bytes=262_144):
        self._controls = deque()
        self._bytes = 0
        self._max_controls = max_controls
        self._max_bytes = max_control_bytes
        self._sample = None
        self._preview = None
        self._ready = asyncio.Event()

    def put_control(self, value):
        size = len(encode_message(value)) + 1024  # Envelope added by the single writer.
        if len(self._controls) >= self._max_controls or self._bytes + size > self._max_bytes:
            raise QueueFull("Control queue is full")
        self._controls.append((copy.deepcopy(value), size))
        self._bytes += size
        self._ready.set()

    def put_sample(self, value):
        encode_message(value)
        self._sample = copy.deepcopy(value)
        self._ready.set()

    def clear_sample(self):
        self._sample = None

    def put_preview(self, value):
        encode_message(value)
        self._preview = copy.deepcopy(value)
        self._ready.set()

    def clear_preview(self):
        self._preview = None

    async def get(self):
        while True:
            if self._controls:
                value, size = self._controls.popleft()
                self._bytes -= size
                return value
            if self._sample is not None:
                value, self._sample = self._sample, None
                return value
            if self._preview is not None:
                value, self._preview = self._preview, None
                return value
            self._ready.clear()
            await self._ready.wait()

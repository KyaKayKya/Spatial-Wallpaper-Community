"""Strict input, canonical JSON profile and atomic local state."""
import json
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]
OFFICIAL = "KyaKayKya/Spatial-Wallpaper"
CHECKER = "market-metadata/1"


class MarketError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise MarketError(message)


def _object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate JSON key: " + key)
        result[key] = value
    return result


def _invalid_number(value):
    raise MarketError("only integers allowed in market JSON: " + value)


def loads(raw):
    return json.loads(raw, object_pairs_hook=_object, parse_float=_invalid_number, parse_constant=_invalid_number)


def canonical(value):
    # Profile v1: UTF-8, sorted keys, compact separators, integers only; no normalization.
    raw = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)
    loads(raw)
    return raw.encode("utf-8")


def read_json(path):
    return loads(Path(path).read_bytes())


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = canonical(value) + b"\n"
    fd, name = tempfile.mkstemp(dir=path.parent, prefix=".market-", suffix=".tmp")
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def validate(name, value):
    schema = read_json(ROOT / "market" / "schemas" / (name + ".schema.json"))
    errors = sorted(Draft202012Validator(schema).iter_errors(value), key=lambda e: str(e.path))
    require(not errors, name + ": " + "; ".join(e.message for e in errors))
    canonical(value)
    return value


def instant(value):
    require(isinstance(value, str) and value.endswith("Z"), "expected UTC timestamp ending Z")
    result = datetime.fromisoformat(value[:-1] + "+00:00")
    require(result.utcoffset().total_seconds() == 0, "expected UTC")
    return result


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def work_key(repository_id, work_id):
    return str(repository_id) + ":" + work_id

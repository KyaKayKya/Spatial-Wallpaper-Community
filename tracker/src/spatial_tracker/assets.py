"""Explicit, hash-checked official model downloads into a local cache."""

import hashlib
from pathlib import Path
import urllib.request

ASSETS = {
    "face_landmarker.task": (
        "https://storage.googleapis.com/mediapipe-models/face_landmarker/"
        "face_landmarker/float16/1/face_landmarker.task",
        "64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff",
    ),
    "canonical_face_model.obj": (
        "https://raw.githubusercontent.com/google-ai-edge/mediapipe/master/"
        "mediapipe/modules/face_geometry/data/canonical_face_model.obj",
        "8bac80443397e113f41a8b565ea72c59390bc031d9defab289dba7bc0c54e618",
    ),
}


def verify_asset(path, name):
    data = Path(path).read_bytes()
    if hashlib.sha256(data).hexdigest() != ASSETS[name][1]:
        raise ValueError(f"SHA-256 mismatch: {name}")
    return Path(path)


def model_paths(root):
    root = Path(root)
    return {name: verify_asset(root / name, name) for name in ASSETS}


def fetch_models(root):
    root = Path(root)
    root.mkdir(parents=True, exist_ok=True)
    for name, (url, expected_hash) in ASSETS.items():
        destination = root / name
        if destination.exists():
            verify_asset(destination, name)
            continue
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read(16 * 1024 * 1024 + 1)
        if len(data) > 16 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != expected_hash:
            raise ValueError(f"SHA-256 mismatch: {name}")
        temporary = root / (name + ".partial")
        temporary.write_bytes(data)
        temporary.replace(destination)
    return model_paths(root)

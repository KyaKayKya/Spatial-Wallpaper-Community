"""Download the official MediaPipe portrait into the ignored test cache."""

import hashlib
from pathlib import Path
import urllib.request

URL = "https://storage.googleapis.com/mediapipe-assets/portrait.jpg"
SHA256 = "a6f11efaa834706db23f275b6115058fa87fc7f14362681e6abe14e82749de3e"


def main():
    destination = Path(".cache/fixtures/portrait.jpg")
    if destination.exists():
        data = destination.read_bytes()
    else:
        with urllib.request.urlopen(URL, timeout=60) as response:
            data = response.read(2 * 1024 * 1024 + 1)
    if hashlib.sha256(data).hexdigest() != SHA256:
        raise ValueError("Portrait SHA-256 mismatch")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)
    print(destination)


if __name__ == "__main__":
    main()

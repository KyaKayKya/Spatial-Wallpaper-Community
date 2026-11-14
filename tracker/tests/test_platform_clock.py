"""A child must report the same native epoch, never its process start time."""

import json
import os
from pathlib import Path
import subprocess
import sys

from spatial_tracker.clock import CLOCK_DOMAIN, qpc_us


def test_clock_cross_process_same_boot_epoch():
    before = qpc_us()
    env = dict(os.environ, PYTHONPATH=str(Path(__file__).resolve().parents[1] / "src"))
    raw = subprocess.check_output(
        [
            sys.executable,
            "-c",
            "from spatial_tracker.clock import CLOCK_DOMAIN,qpc_us; import json; "
            "print(json.dumps([CLOCK_DOMAIN,qpc_us()]))",
        ],
        env=env,
        text=True,
    )
    after = qpc_us()
    domain, timestamp = json.loads(raw)
    assert domain == CLOCK_DOMAIN
    assert before <= timestamp <= after


def test_native_camera_clock_matches_protocol_clock():
    import pytest
    from spatial_tracker.camera_macos import load_native, native_path

    if sys.platform != "darwin" or not native_path().is_file():
        pytest.skip("requires the built macOS native library")
    native = load_native()
    before = qpc_us()
    capture_clock = native.SWCameraNow()
    after = qpc_us()
    assert before <= capture_clock <= after

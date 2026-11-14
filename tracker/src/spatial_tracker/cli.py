"""Developer entry points; the packaged worker receives credentials only on stdin."""

import argparse
import asyncio
import json
from pathlib import Path
import sys
import uuid

from .assets import fetch_models
from .clock import qpc_us
from .estimator import PoseEstimator, stream_descriptor
from .worker import TrackerWorker, validate_bootstrap


async def _analyze_image(path, model_dir):
    import cv2

    image = cv2.imread(str(path))
    if image is None:
        raise ValueError("Image cannot be decoded")
    rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    samples = asyncio.Queue()
    estimator = PoseEstimator(model_dir, samples.put_nowait)
    stream = str(uuid.uuid4())
    try:
        estimator.set_stream(stream)
        estimator.submit(stream, 1, qpc_us(), rgb)
        sample = await asyncio.wait_for(samples.get(), 15)
        h, w = rgb.shape[:2]
        mode = {
            "mode_id": None,
            "source_width_px": w,
            "source_height_px": h,
            "width_px": w,
            "height_px": h,
            "fps": 0,
            "pixel_format": "RGB8",
            "rotation_deg": 0,
            "crop_rect": [0, 0, w, h],
            "mirrored": False,
        }
        return {
            "stream_descriptor": stream_descriptor(
                stream,
                mode,
                {"camera_id": "offline-image", "intent_revision": "0"},
                str(uuid.uuid4()),
            ),
            "sample": sample,
        }
    finally:
        estimator.close()


def camera_backend():
    if sys.platform == "win32":
        from .camera import WindowsCamera
        return WindowsCamera()
    if sys.platform == "darwin":
        from .camera_macos import MacCamera
        return MacCamera()
    raise RuntimeError("Camera capture supports Windows/macOS")


async def _devices():
    return {"catalog_ready": True, "devices": await camera_backend().catalog()}


async def _run(config, model_dir):
    options = {} if sys.platform == "win32" else {"monitor_factory": None}
    instance = TrackerWorker(camera_backend(), lambda callback: PoseEstimator(model_dir, callback),
                             **options)
    await instance.run(config)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Spatial Wallpaper camera worker")
    parser.add_argument("--model-dir", type=Path, default=Path(".cache/models"))
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("devices", help="Enumerate cameras without opening one")
    commands.add_parser("fetch-models", help="Download and verify the two pinned model assets")
    image = commands.add_parser("analyze-image", help="Run real offline inference without a camera")
    image.add_argument("path", type=Path)
    commands.add_parser(
        "run", help="Read one bootstrap JSON line from stdin and connect to Runtime"
    )
    simulation = commands.add_parser("simulate", help="Camera-free measurement source over real worker TCP")
    simulation.add_argument("--recording", type=Path)
    simulation.add_argument("--scenario", choices=("motion", "faults", "fixed"), default="motion")
    args = parser.parse_args(argv)
    try:
        if args.command == "fetch-models":
            result = {name: str(path) for name, path in fetch_models(args.model_dir).items()}
        elif args.command == "devices":
            result = asyncio.run(_devices())
        elif args.command == "analyze-image":
            result = asyncio.run(_analyze_image(args.path, args.model_dir))
        else:
            line = sys.stdin.buffer.readline(8193)
            if len(line) > 8192 or not line.endswith(b"\n"):
                raise ValueError("Bootstrap must be one bounded JSON line")
            config = validate_bootstrap(json.loads(line))
            if args.command == "simulate":
                from .simulation import run_simulation

                asyncio.run(run_simulation(config, args.recording, args.scenario))
            else:
                asyncio.run(_run(config, args.model_dir))
            return 0
        print(json.dumps(result, ensure_ascii=False, allow_nan=False))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception as exc:
        # Bootstrap contents, native device paths and credentials never go to diagnostics.
        print(
            f"Tracker command failed ({type(exc).__name__}); check configuration and model files.",
            file=sys.stderr,
        )
        return 2

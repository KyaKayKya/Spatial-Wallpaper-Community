"""Launch this worktree's real Unity player and owned Tracker over the v1 TCP contract."""
import argparse
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("--scenario", choices=("motion", "faults"), default="motion")
parser.add_argument("--seconds", type=float, default=15)
parser.add_argument("--frames", action="store_true")
parser.add_argument("--recording", type=Path)
parser.add_argument("--output", type=Path)
default_python = root / "tracker/.venv/Scripts/python.exe"
if not default_python.exists():
    default_python = root.parent.parent / "tracker/.venv/Scripts/python.exe"
parser.add_argument("--python", type=Path, default=default_python)
args = parser.parse_args()
output = (args.output or root / "Validation/Integration" / args.scenario).resolve()
output.mkdir(parents=True, exist_ok=True)
command = [
    str(root / "Prototype/Builds/Showcase/Showcase.exe"),
    "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0",
    "-logFile", str(output / "player.log"), "--tracking",
    "--tracking-python", str(args.python.resolve()), "--tracking-root", str(root / "tracker"),
    "--tracking-mode", "simulate", "--tracking-scenario", args.scenario,
    "--tracking-output", str(output), "--tracking-seconds", str(args.seconds)]
if args.frames:
    command.append("--tracking-frames")
if args.recording:
    command += ["--tracking-recording", str(args.recording.resolve())]
result = subprocess.run(command)
print("Player exit", result.returncode, "evidence", output)
sys.exit(result.returncode)

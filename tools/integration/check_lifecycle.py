"""Exercise Unity component and GameObject disable/re-enable in batch Play Mode."""
from pathlib import Path
import os
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
env = dict(os.environ)
env.setdefault("ALLUSERSPROFILE", env.get("PROGRAMDATA", "C:/ProgramData"))
python = root / "tracker/.venv/Scripts/python.exe"
if not python.exists():
    python = root.parent.parent / "tracker/.venv/Scripts/python.exe"
args = [
    "D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe", "-batchmode",
    "-projectPath", str(root / "Prototype"), "-executeMethod", "TrackingPlayChecks.Run",
    "-logFile", str(root / "Validation/Integration/lifecycle.log"), "--tracking",
    "--tracking-python", str(python),
    "--tracking-root", str(root / "tracker")]
result = subprocess.run(args, env=env)
print("Unity lifecycle exit", result.returncode)
sys.exit(result.returncode)

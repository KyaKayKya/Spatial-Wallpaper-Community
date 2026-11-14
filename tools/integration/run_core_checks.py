"""Compile and run the same C# consumer core used by Unity, without the editor."""
from pathlib import Path
import shutil
import os
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
data = Path(os.environ.get("UNITY_EDITOR_DATA", "D:/Unity/Editor/2022.3.62f3/Editor/Data"))
mono = data / ("MonoBleedingEdge/bin/mono.exe" if sys.platform == "win32" else "MonoBleedingEdge/bin/mono")
out = root / "Validation/Integration/local-core"
out.mkdir(parents=True, exist_ok=True)
newtonsoft = data / "Managed/Newtonsoft.Json.dll"
shutil.copy2(newtonsoft, out / newtonsoft.name)
sources = list((root / "Prototype/Assets/Tracking/Core").glob("*.cs"))
command = [str(mono),
           str(data / "MonoBleedingEdge/lib/mono/4.5/csc.exe"),
           "-nologo", "-langversion:latest", "-r:" + str(newtonsoft),
           "-r:Microsoft.CSharp.dll", "-r:" + str(data / "MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"), "-out:" + str(out / "CoreChecks.exe"),
           str(root / "tools/integration/CoreChecks.cs"), str(root / "tools/integration/HostChecks.cs"), str(root / "tools/integration/SmoothingChecks.cs"),
           "-main:" + ("HostChecks" if "--host" in sys.argv else "SmoothingChecks" if "--smooth" in sys.argv else "CoreChecks"), *map(str, sources)]
result = subprocess.run(command, capture_output=True, text=True)
print(result.stdout, result.stderr)
if result.returncode:
    sys.exit(result.returncode)
args = [str(mono), str(out / "CoreChecks.exe")]
if "--host" in sys.argv:
    python = Path(os.environ.get("SPATIAL_TEST_PYTHON",str(root / ("tracker/.venv/Scripts/python.exe" if sys.platform=="win32" else "tracker/.venv/bin/python"))))
    if not python.exists():
        python = root.parent.parent / ("tracker/.venv/Scripts/python.exe" if sys.platform=="win32" else "tracker/.venv/bin/python")
    args += [str(python), str(root / "tracker"),
             str(root / "Prototype/Assets/StreamingAssets/tracking-calibration-synthetic.json")]
if "--smooth" in sys.argv:
    args += [str(root / "Validation/Integration/smoothing.json")]
result = subprocess.run(args, capture_output=True, text=True)
print(result.stdout, result.stderr)
sys.exit(result.returncode)

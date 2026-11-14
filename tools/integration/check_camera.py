"""Inspect safe Tracker status through the same authenticated Runtime host as Unity."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
root = Path(__file__).resolve().parents[2]
data = Path(os.environ.get('UNITY_EDITOR_DATA', 'D:/Unity/Editor/2022.3.62f3/Editor/Data'))
out = root / 'Validation/Calibration/core'
out.mkdir(parents=True, exist_ok=True)
dll = data / 'Managed/Newtonsoft.Json.dll'
shutil.copy2(dll, out / dll.name)
mono = data / 'MonoBleedingEdge/bin/mono.exe'
subprocess.run([str(mono), str(data / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', '-r:' + str(dll), '-r:Microsoft.CSharp.dll', '-r:' + str(data / 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'), '-out:' + str(out / 'CameraCheck.exe'), str(root / 'tools/integration/CameraCheck.cs'), *map(str, (root / 'Prototype/Assets/Tracking/Core').glob('*.cs'))], check=True)
python = root / 'tracker/.venv/Scripts/python.exe'
if not python.exists():
    python = root.parent.parent / 'tracker/.venv/Scripts/python.exe'
raise SystemExit(subprocess.call([str(mono), str(out / 'CameraCheck.exe'), str(python), str(root / 'tracker'), sys.argv[1]]))

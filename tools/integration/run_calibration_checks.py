"""Run calibration domain checks with Unity's Mono, without launching the editor."""
from pathlib import Path
import os
import sys
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
data = Path(os.environ.get('UNITY_EDITOR_DATA', 'D:/Unity/Editor/2022.3.62f3/Editor/Data'))
out = root / 'Validation/Calibration/core'
out.mkdir(parents=True, exist_ok=True)
json = data / 'Managed/Newtonsoft.Json.dll'
shutil.copy2(json, out / json.name)
mono = data / ('MonoBleedingEdge/bin/mono.exe' if sys.platform == 'win32' else 'MonoBleedingEdge/bin/mono')
sources = list((root / 'Prototype/Assets/Tracking/Core').glob('*.cs'))
sources += list((root / 'Prototype/Assets/Calibration/Core').glob('*.cs'))
subprocess.run([str(mono), str(data / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-r:' + str(json),
               '-r:' + str(data / 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),
               '-out:' + str(out / 'CalibrationChecks.exe'),
               str(root / 'tools/integration/CalibrationChecks.cs'), *map(str, sources)], check=True)
subprocess.run([str(mono), str(out / 'CalibrationChecks.exe')], check=True)

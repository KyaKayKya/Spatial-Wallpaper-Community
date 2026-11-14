"""Developer check against the actual Unity-independent calibration consumer."""
import os
from pathlib import Path
import shutil
import subprocess
import sys

root=Path(__file__).resolve().parents[2]
editor=Path(os.environ.get('UNITY_EDITOR_DATA','D:/Unity/Editor/2022.3.62f3/Editor/Data'))
output=root/'Validation/Gaze/local/core'; output.mkdir(parents=True,exist_ok=True)
dll=editor/'Managed/Newtonsoft.Json.dll'; shutil.copy2(dll,output/dll.name)
mono=editor/'MonoBleedingEdge/bin/mono.exe'
sources=[*list((root/'Prototype/Assets/Tracking/Core').glob('*.cs')),
         *list((root/'Prototype/Assets/Calibration/Core').glob('*.cs')),
         root/'tools/integration/ValidateCalibrationProfile.cs']
subprocess.run([str(mono),str(editor/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',
                '-r:'+str(dll),'-r:'+str(editor/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),
                '-out:'+str(output/'ValidateCalibrationProfile.exe'),*map(str,sources)],check=True)
subprocess.run([str(mono),str(output/'ValidateCalibrationProfile.exe'),str(Path(sys.argv[1]).resolve())],check=True)

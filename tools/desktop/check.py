"""Run shared C# checks on either host; Windows native checks run only on Windows."""
from pathlib import Path
import argparse
import os
import subprocess
import sys
import shutil

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('check', nargs='?')
parser.add_argument('--unity-data', type=Path, default=os.environ.get('UNITY_EDITOR_DATA',os.environ.get('UNITY_DATA')))
parser.add_argument('--mono', type=Path)
parser.add_argument('--compiler', type=Path)
parser.add_argument('--json-dll', type=Path)
args = parser.parse_args()
data = args.unity_data or (Path('D:/Unity/Editor/2022.3.62f3/Editor/Data') if sys.platform == 'win32' else None)
if data is None and not (args.mono and args.compiler and args.json_dll):
    parser.error('Set UNITY_DATA/--unity-data or --mono, --compiler, --json-dll explicitly')
mono = args.mono or data / ('MonoBleedingEdge/bin/mono.exe' if sys.platform == 'win32' else 'MonoBleedingEdge/bin/mono')
compiler = args.compiler or data / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
json_dll = args.json_dll or data / 'Managed/Newtonsoft.Json.dll'
out = root / 'Validation/Desktop/core'
out.mkdir(parents=True, exist_ok=True)
shutil.copy2(json_dll, out / json_dll.name)
checks = ['DesktopPolicyChecks', 'DesktopTrackingChecks', 'DesktopClientChecks', 'DesktopLeaseChecks', 'RainyCafeClockChecks', 'PlatformClockChecks']
if sys.platform == 'win32':
    checks.insert(0, 'DesktopNativeChecks')
if args.check and args.check not in checks:
    parser.error('Unknown or unsupported check for this host: ' + args.check)
for name in checks:
    if args.check and args.check != name:
        continue
    sources = [root / 'tools/desktop' / (name + '.cs')]
    if name == 'RainyCafeClockChecks':
        sources += [root / 'Prototype/Assets/RainyCafe/Runtime/RainyCafeParticleWeather.cs']
    elif name == 'DesktopNativeChecks':
        sources += [root / 'Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs', root / 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs', root / 'Prototype/Assets/Desktop/Core/IDesktopShellState.cs', root / 'Prototype/Assets/Desktop/Windows/WindowsDesktopShell.cs']
    elif name == 'DesktopPolicyChecks':
        sources += [root / 'Prototype/Assets/Desktop/Core/DesktopPolicy.cs']
    elif name == 'DesktopLeaseChecks':
        sources += [root / 'Prototype/Assets/Desktop/Core/DesktopManagerLease.cs', root / 'Prototype/Assets/Tracking/Core/RuntimeLease.cs', root / 'Prototype/Assets/Desktop/Core/DesktopLifetime.cs']
    else:
        sources += list((root / 'Prototype/Assets/Tracking/Core').glob('*.cs'))
        sources += [root / 'Prototype/Assets/Desktop/Core/DesktopTrackingState.cs']
    if name == 'DesktopClientChecks':
        sources += list((root / 'Prototype/Assets/Calibration/Core').glob('*.cs'))
        sources += [root / p for p in ['Prototype/Assets/Desktop/DesktopHostClient.cs', 'Prototype/Assets/ContentRuntime/Runtime/ContentHostClient.cs', 'Prototype/Assets/Library/Core/IWallpaperHost.cs','Prototype/Assets/Library/Core/ArtworkCatalog.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/SoundMix.cs']]
    target = out / (name + '.exe')
    references = ['-r:' + str(json_dll)]
    if data:
        references += ['-r:' + str(data / 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll')]
    subprocess.run([str(mono), str(compiler), '-nologo', '-langversion:latest', *references,
                    '-out:' + str(target), *map(str, sources)], check=True)
    subprocess.run([str(mono), str(target), str(out)], check=True)

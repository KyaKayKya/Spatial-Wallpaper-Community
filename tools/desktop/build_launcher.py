"""Build the Windows GUI entry; the compiler host and destination paths are explicit."""
from pathlib import Path
import argparse
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path)
parser.add_argument('--compiler', type=Path, default=os.environ.get('SPATIAL_LAUNCHER_COMPILER'))
parser.add_argument('--mono', type=Path, default=os.environ.get('SPATIAL_LAUNCHER_MONO'))
parser.add_argument('--tracking-python', default=os.environ.get('SPATIAL_WINDOWS_TRACKING_PYTHON'))
args = parser.parse_args()
main = Path(subprocess.check_output(['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'], cwd=root, text=True).strip()).parent
if args.compiler is None:
    if sys.platform == 'win32':
        args.compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    else:
        parser.error('Cross-build Windows launcher requires --compiler and --mono')
if not args.compiler.is_file():
    parser.error('C# compiler not found: ' + str(args.compiler))
if args.tracking_python is None:
    if sys.platform != 'win32':
        parser.error('Cross-build requires --tracking-python with its destination Windows path')
    args.tracking_python = str(main / 'tracker/.venv/Scripts/python.exe')
target = args.output.resolve() if args.output else root / 'Prototype/Builds/Library/Launch.exe'
target.parent.mkdir(parents=True, exist_ok=True)
command = ([str(args.mono)] if args.mono else []) + [str(args.compiler), '/nologo', '/target:winexe', '/optimize+',
    '/reference:System.Windows.Forms.dll', '/reference:System.Configuration.dll',
    '/out:' + str(target), str(root / 'tools/desktop/Launcher.cs')]
subprocess.run(command, check=True)
config = ET.Element('configuration')
settings = ET.SubElement(config, 'appSettings')
ET.SubElement(settings, 'add', key='TrackingPython', value=args.tracking_python)
ET.ElementTree(config).write(str(target) + '.config', encoding='utf-8', xml_declaration=True)
print('Built Windows GUI launcher', target)

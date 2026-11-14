"""Compile real Player window ownership checks without Unity or UI automation."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/sdk'))
from check_contracts import DATA
from windows_framework import references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--supervisor', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--owner-process', action='store_true')
    parser.add_argument('--desktop-target', action='store_true')
    parser.add_argument('--delay-script-pin', action='store_true', help='test-only source fixture: script may exit before its parent pin is acquired')
    parser.add_argument('--source-override', type=Path, help='retained task3 parent source for a scheduling regression negative')
    args = parser.parse_args()
    assert not args.delay_script_pin or args.owner_process
    assert args.source_override is None or args.owner_process
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    for name in ('Supervisor.exe', 'Newtonsoft.Json.dll'):
        shutil.copyfile(args.supervisor / name, out / name)
    assert not (args.owner_process and args.desktop_target)
    name = 'NativeDesktopTargetChecks' if args.desktop_target else ('NativeRenderOwnerChecks' if args.owner_process else 'NativeWindowOwnershipChecks')
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', out / (name + '.exe.config'))
    sources = [ROOT / ('tools/desktop/' + name + '.cs'),
               ROOT / 'Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs',
               ROOT / 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
    if args.owner_process or args.desktop_target:
        source = ROOT / 'Prototype/Assets/Desktop/Windows/NativeRenderProcess.cs'
        if args.source_override:
            source = args.source_override.resolve()
            assert source.is_relative_to(ROOT / 'Validation/Content/sdk-runtime/native-render/task3') and source.name == 'NativeRenderProcess.cs'
        if args.delay_script_pin:
            text = source.read_text(encoding='utf-8')
            needle = 'var pin=PinnedProcessOwner.RetainOriginal(sample,creation);'
            if needle not in text:
                # Earlier precise build: move its outer delay inside the
                # Process-object -> first-kernel-handle acquisition window.
                text = text.replace('Thread.Sleep(300);script=PinObservedScript', 'script=PinObservedScript')
                needle = 'long actual=PinnedProcessOwner.ReadCreationFileTime(sample.Handle);'
            assert text.count(needle) == 1
            mutated = out / source.name
            mutated.write_text(text.replace(needle, 'Thread.Sleep(300);' + needle), encoding='utf-8')
            source = mutated
        sources.append(source)
    if args.desktop_target:
        sources.append(ROOT / 'Prototype/Assets/Desktop/Windows/NativeDesktopTarget.cs')
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', *references(), '-r:' + str(out / 'Supervisor.exe'), '-r:' + str(out / 'Newtonsoft.Json.dll'),
               '-out:' + str(out / (name + '.exe')), *map(str, sources)]
    result = subprocess.run(command, capture_output=True, text=True)
    (out / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    result.check_returncode()
    (out / 'build.json').write_text(json.dumps({'scope': 'window-owner-check-only',
        'test_delay_before_script_pin_ms': 300 if args.delay_script_pin else 0,
        'source_override': str(args.source_override.resolve()) if args.source_override else None,
        'sources': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},
        'files': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.suffix in ('.exe', '.dll')}}, indent=2), encoding='utf-8')
    print(out)


if __name__ == '__main__':
    main()

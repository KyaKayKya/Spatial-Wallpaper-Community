"""Trusted preview UI accumulation; not OS-input or Library acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/sdk'))
from check_contracts import DATA
from windows_framework import references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--supervisor', type=Path, required=True)
    args = parser.parse_args()
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/preview-interaction' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    shutil.copyfile(args.supervisor / 'Supervisor.exe', output / 'Supervisor.exe')
    shutil.copyfile(args.supervisor / 'Newtonsoft.Json.dll', output / 'Newtonsoft.Json.dll')
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', output / 'Checks.exe.config')
    sources = [ROOT / 'tools/desktop/NativePreviewInteractionChecks.cs', ROOT / 'Prototype/Assets/Desktop/Core/NativePreviewInteractionTarget.cs']
    frozen = sources + [Path(__file__), output / 'Supervisor.exe', output / 'Newtonsoft.Json.dll', output / 'Checks.exe.config']
    inventory = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen if path.exists()}
    report = {'status': 'failed', 'scope': __doc__, 'inputs': inventory}
    try:
        command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', *references(), '-r:' + str(output / 'Supervisor.exe'), '-out:' + str(output / 'Checks.exe'), *map(str, sources)]
        for name, command in [('compile', command), ('run', [str(output / 'Checks.exe')])]:
            result = subprocess.run(command, capture_output=True, text=True, timeout=20)
            (output / (name + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
            report[name + '_exit_code'] = result.returncode
            print(result.stdout + result.stderr)
            if result.returncode:
                return result.returncode
        report['inputs_unchanged'] = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in inventory.items())
        report['assembly_sha256'] = hashlib.sha256((output / 'Checks.exe').read_bytes()).hexdigest()
        report['status'] = 'passed-trusted-preview-ui-only' if report['inputs_unchanged'] else 'failed'
        return 0 if report['inputs_unchanged'] else 1
    finally:
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    raise SystemExit(main())

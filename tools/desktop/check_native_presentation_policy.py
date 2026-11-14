"""Per-target rational 60Hz cadence policy; no physical display or GPU claim."""
import hashlib
import json
import pathlib
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/sdk'))
from check_contracts import DATA
from windows_framework import references


def main():
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/presentation-policy' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    sources = [ROOT / 'tools/desktop/NativePresentationPolicyChecks.cs', ROOT / 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
    inventory = {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    report = {'status': 'failed', 'scope': __doc__, 'sources_sha256': inventory}
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', *references(), '-out:' + str(output / 'Checks.exe'), *map(str, sources)]
    try:
        for name, arguments in [('compile', command), ('result', [str(output / 'Checks.exe')])]:
            result = subprocess.run(arguments, capture_output=True, text=True, timeout=15)
            (output / (name + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
            print(result.stdout + result.stderr)
            report[name + '_exit_code'] = result.returncode
            if result.returncode:
                return result.returncode
        report['status'] = 'passed-pure-presentation-policy'
        return 0
    finally:
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    raise SystemExit(main())

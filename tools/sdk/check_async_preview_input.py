"""Bounded asynchronous preview transport checks; no native presentation claim."""
import hashlib
import json
import subprocess
import uuid
from pathlib import Path

from check_contracts import ROOT, DATA
from windows_framework import references


def main():
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/async-preview' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    sources = [ROOT / 'tools/sdk/AsyncPreviewInputChecks.cs']
    sources += [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities' / name for name in (
        'InputBroker.cs', 'InputGate.cs', 'InputSnapshot.cs', 'WindowsMachineClock.cs', 'AsyncPreviewInputTarget.cs')]
    sources += [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']
    inventory = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources if path.exists()}
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', *references(), '-out:' + str(output / 'Checks.exe'), *map(str, sources)]
    report = {'status': 'failed', 'scope': __doc__, 'sources': inventory}
    try:
        for name, arguments in [('compile', command), ('result', [str(output / 'Checks.exe')])]:
            result = subprocess.run(arguments, capture_output=True, text=True, timeout=15)
            (output / (name + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
            print(result.stdout + result.stderr)
            report[name + '_exit_code'] = result.returncode
            if result.returncode:
                return result.returncode
        assert all(hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == digest for path, digest in inventory.items())
        report['status'] = 'passed-async-transport-only'
        report['assembly_sha256'] = hashlib.sha256((output / 'Checks.exe').read_bytes()).hexdigest()
        return 0
    finally:
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    raise SystemExit(main())

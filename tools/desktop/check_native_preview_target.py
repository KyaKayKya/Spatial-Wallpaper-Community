"""Actual native preview window owner lifecycle; not original Library or RC."""
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
from case_process import Job
from check_contracts import DATA
from windows_framework import FRAMEWORK, references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--supervisor', type=Path, required=True)
    parser.add_argument('--player', type=Path, required=True)
    args = parser.parse_args()
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/preview-target' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    sources = [ROOT / name for name in ['tools/desktop/NativePreviewTargetChecks.cs', 'Prototype/Assets/Desktop/Windows/NativePreviewTarget.cs', 'Prototype/Assets/Desktop/Windows/NativeRenderProcess.cs', 'Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs', 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']]
    for name in ['Supervisor.exe', 'Newtonsoft.Json.dll']:
        shutil.copyfile(args.supervisor / name, output / name)
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', output / 'Checks.exe.config')
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', '-platform:x64', *references(), '-r:' + str(FRAMEWORK / 'System.Drawing.dll'), '-r:' + str(FRAMEWORK / 'System.Windows.Forms.dll'), '-r:' + str(output / 'Supervisor.exe'), '-r:' + str(output / 'Newtonsoft.Json.dll'), '-out:' + str(output / 'Checks.exe'), *map(str, sources)]
    report = {'scope': __doc__, 'status': 'failed'}
    frozen = sources + [Path(__file__), ROOT / 'tools/sdk/case_process.py'] + [path for path in args.player.parent.rglob('*') if path.is_file()] + [output / name for name in ['Supervisor.exe', 'Newtonsoft.Json.dll', 'Checks.exe.config']]
    inventory = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen if path.exists()}
    report['sources_sha256_before_compile'] = inventory
    job, process = Job(), None
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=30)
        (output / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        report['compile_exit_code'] = result.returncode
        if result.returncode:
            print(result.stdout + result.stderr)
            return result.returncode
        report['inputs_unchanged_during_compile'] = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in inventory.items())
        if not report['inputs_unchanged_during_compile']:
            return 1
        report['compiled_sha256'] = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in output.iterdir() if path.suffix in ['.exe', '.dll', '.config']}
        inventory.update(report['compiled_sha256'])
        with (output / 'result.log').open('w', encoding='utf-8') as log:
            process = subprocess.Popen([str(output / 'Checks.exe'), str(args.player.resolve()), str(output / 'run')], stdin=subprocess.PIPE, stdout=log, stderr=log, text=True)
            job.assign(process)
            process.stdin.write('RUN\n')
            process.stdin.close()
            report['exit_code'] = process.wait(timeout=100)
        report['inputs_unchanged'] = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in inventory.items())
        report['status'] = 'passed-native-preview-target-development' if report['exit_code'] == 0 and report['inputs_unchanged'] else 'failed'
        print((output / 'result.log').read_text(encoding='utf-8'))
    finally:
        report['job_active_before_cleanup'] = job.active()
        job.terminate()
        report['job_active_after_cleanup'] = job.active()
        job.close()
        if report['job_active_before_cleanup'] or report['job_active_after_cleanup']:
            report['status'] = 'failed-owned-job-fallback'
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return 0 if report['status'] == 'passed-native-preview-target-development' else 1


if __name__ == '__main__':
    raise SystemExit(main())

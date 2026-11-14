"""Pure controlled Host-status tests; no native input, Host, Worker or Unity."""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references, FRAMEWORK
from provenance import sources

out = ROOT/'Validation/Content/sdk-runtime/E/native-lifecycle'/uuid.uuid4().hex
out.mkdir(parents=True)
receipt = sources()
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll', out/'Newtonsoft.Json.dll')
command = [str(DATA/'MonoBleedingEdge/bin/mono.exe'), str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
           '-nologo', '-langversion:latest', *references(), '-r:'+str(out/'Newtonsoft.Json.dll'),
           '-out:'+str(out/'Checks.exe'), str(ROOT/'Supervisor/Tests/NativeHostLifecycle.cs'),
           str(ROOT/'Supervisor/Tests/NativeHostLifecycleChecks.cs')]
compiled = subprocess.run(command, capture_output=True, text=True)
(out/'compile.log').write_text(compiled.stdout+compiled.stderr, encoding='utf-8')
print(out, flush=True)
compiled.check_returncode()
result = subprocess.run([str(out/'Checks.exe')], capture_output=True, text=True, timeout=10)
(out/'checks.log').write_text(result.stdout+result.stderr, encoding='utf-8')
print(result.stdout+result.stderr)
report = {'status': 'passed' if result.returncode == 0 else 'failed',
          'scope': 'controlled-status-not-real-Host', 'sources': receipt,
          'source_unchanged': sources() == receipt, 'exit_code': result.returncode}
(out/'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
result.check_returncode()
assert report['source_unchanged']
# Compile the actual desktop Client without executing any of its native code.
from native_input_client_sources import native_input_client_sources
files = native_input_client_sources(ROOT)
report["client_sources"] = [p.relative_to(ROOT).as_posix() for p in files]
compile_client = subprocess.run([*command[:command.index('-r:'+str(out/'Newtonsoft.Json.dll'))],
    *['-r:'+str(FRAMEWORK/name) for name in ('Microsoft.CSharp.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll')],
    '-r:'+str(out/'Newtonsoft.Json.dll'), '-out:'+str(out/'Client-compile-only.exe'), *map(str, files)],
    capture_output=True, text=True)
(out/'client-compile.log').write_text(compile_client.stdout+compile_client.stderr, encoding='utf-8')
report['native_client_compiled_not_executed'] = compile_client.returncode == 0
report['source_unchanged'] = sources() == receipt
if compile_client.returncode or not report['source_unchanged']:
    report['status'] = 'failed'
(out/'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
compile_client.check_returncode()
assert report['source_unchanged']

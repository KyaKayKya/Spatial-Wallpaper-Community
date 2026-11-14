"""Check trusted application audio selection identity, without capturing system mix."""
from pathlib import Path
import json,shutil,subprocess,uuid
from check_contracts import ROOT,DATA
from windows_framework import references
from toolchain_process import run_toolchain

out=ROOT/'Validation/Content/sdk-runtime/G/audio-selection'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True);report={'status':'failed'}
try:
    shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
    shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
    files=[ROOT/'Supervisor/Tests/AudioSelectionChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/TrustedAudioSource.cs']
    done=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],capture_output=True,text=True)
    (out/'compile.log').write_text(done.stdout+done.stderr);done.check_returncode();report['process']={}
    with (out/'checks.log').open('w') as log:run_toolchain([out/'Checks.exe',out/'result.json'],ROOT,log,20,report['process'])
    report.update(status='passed-development-trusted-audio-selection',result=json.loads((out/'result.json').read_text()))
finally:
    (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

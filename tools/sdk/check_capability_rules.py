"""Compile actual facade/binding classes; no Unity, Worker process or native input."""
import hashlib
import json
import shutil
import subprocess
import uuid
from check_contracts import ROOT, DATA, OUT, compile_checks
from provenance import sources
from windows_framework import references


def main():
    out=ROOT/'Validation/Content/sdk-runtime/G/capability-rules'/uuid.uuid4().hex
    out.mkdir(parents=True);print(out,flush=True)
    receipt=sources();report={'status':'failed','sources':receipt}
    try:
        if compile_checks():raise RuntimeError('shared validator compilation failed')
        for name in ['ContractChecks.exe','Newtonsoft.Json.dll']:shutil.copyfile(OUT/name,out/name)
        runtime=ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'
        files=[ROOT/'tools/sdk/CapabilityRuleChecks.cs',runtime/'Storage.cs',runtime/'Llm.cs',runtime/'Hosting/WorkerFrameCodec.cs',
               *[runtime/'Resources'/(name+'.cs') for name in ['ResourceLedger','ManagedReservation','AllocationFrame']],
               *list((runtime/'Capabilities').rglob('*.cs'))]
        command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(out/'ContractChecks.exe'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
        for name,args in [('compile',command),('result',[str(out/'Checks.exe'),str(out/'bindings.json')])]:
            result=subprocess.run(args,capture_output=True,text=True,timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
            (out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
        if sources()!=receipt:raise RuntimeError('capability rules source drift')
        report.update(status='passed-static-capability-rule-closure',result=json.loads((out/'bindings.json').read_text(encoding='utf-8-sig')),
                      binary_sha256=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),
                      shared_validator_sha256=hashlib.sha256((out/'ContractChecks.exe').read_bytes()).hexdigest())
        return 0
    except Exception as error:
        report['error']=repr(error);return 1
    finally:
        (out/'report.json').write_text(json.dumps(report,indent=2))


if __name__=='__main__':raise SystemExit(main())

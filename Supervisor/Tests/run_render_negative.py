"""Prove a known render mutation fails in the actual restricted Worker."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
from run_production import ROOT, DATA, packages
from provenance import sources
from windows_framework import references
from failure_report import failure_receipt
from render_negative_evidence import require_negative

def compile_host(out):
    shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
    files=[ROOT/'Supervisor/Tests/RenderNegativeChecks.cs']
    files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ('WorkerFrameCodec.cs','RuntimeLimits.Generated.cs','WorkerExitCodes.cs')]
    for folder in ('Packages/com.spatialwallpaper.sdk/Runtime/Capabilities','Supervisor/Broker','Supervisor/Core','Supervisor/Windows','Supervisor/Hosting'):
        files+=list((ROOT/folder).glob('*.cs'))
    subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
    shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--build',required=True,type=Path);args=parser.parse_args()
    build=args.build.resolve();receipt=json.loads((build/'report.json').read_text())
    if receipt['status']!='diagnostic-render-negative-only' or receipt['release_candidate'] is not False:raise ValueError('not-render-negative-build')
    actual={p.relative_to(build/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (build/'player').rglob('*') if p.is_file()}
    if actual!=receipt['runtime_files']:raise ValueError('render-negative-image-drift')
    source=sources()
    if source!=receipt['sources']:raise ValueError('render-negative-source-drift')
    out=ROOT/'Validation/Content/sdk-runtime/render-negative'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
    with failure_receipt(out/'report.json',dict(sources=source,worker_build=receipt)):
        ids=packages(out)
        subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
        compile_host(out)
        with (out/'stdout.txt').open('w') as stdout,(out/'stderr.txt').open('w') as stderr:
            subprocess.run([str(out/'Checks.exe'),str(out),str(build/'report.json'),str(build/'player'),str(out/'healthy.spatial'),ids[0],str(ROOT/'Validation/Sdk'/('rneg'+out.name[:10]))],stdout=stdout,stderr=stderr,timeout=90,check=True)
        result=json.loads((out/'results.json').read_text());assert result['status']=='expected-render-mutation-rejected'
        marker=require_negative((out/'worker.log').read_text(encoding='utf-8-sig'),receipt['mode'],result['attempt'],result)
        assert sources()==source,'render-negative-source-changed'
        report=dict(status='diagnostic-render-negative-verified-not-release-acceptance',sources=source,worker_build=receipt,results=result,mutation=marker,
                    host_sha256=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),package_sha256=hashlib.sha256((out/'healthy.spatial').read_bytes()).hexdigest())
        (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()

"""Verify configured independent Host through authenticated management IPC and real HTTP."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,time
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references
from llm_fixture import LlmFixture
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--author',type=Path,required=True);args=parser.parse_args();worker=args.worker.resolve();author=args.author.resolve()
out=ROOT/'Validation/Content/sdk-runtime/E/llm-host'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
wr=json.loads((worker/'report.json').read_text());ar=json.loads((author/'report.json').read_text());assert hashlib.sha256((author/'scene.spatial').read_bytes()).hexdigest()==ar['package_sha256']
assert {p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}==wr['runtime_files']
for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(out/name)],check=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');files=[ROOT/'Supervisor/Tests/LlmHostChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
files+=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','WorkerExitCodes.cs','RuntimeLimits.Generated.cs']]
files += [ROOT/'Supervisor/Windows'/name for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Client.exe'),*map(str,files)],check=True)
fixture=LlmFixture();state=out/'state';config=out/'config.json';config.write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(ROOT/'Validation/Sdk'/('j'+out.name[:10])),'width':640,'height':360,'llm':fixture.config}));host=None
try:
 with (out/'host.log').open('w') as log:
  host=subprocess.Popen([str(out/'supervisor/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+15
  while not (state/'endpoint.json').exists():
   assert host.poll() is None and time.monotonic()<deadline,'configured Host did not publish endpoint';time.sleep(.02)
  result=subprocess.run([str(out/'Client.exe'),str(out),str(author/'scene.spatial'),ar['author']['build'],str(host.pid)],capture_output=True,text=True,timeout=35);(out/'client.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode();assert host.wait(5)==0
 assert fixture.requests and all(r['test_auth_matches'] and r['body']['model']=='fixture-model' for r in fixture.requests)
finally:
 if host is not None and host.poll() is None:host.terminate();host.wait(10)
 fixture.close()
assert sources()==receipt,'LLM Host source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-configured-LLM-Host','sources':receipt,'worker_report_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'author_report_sha256':hashlib.sha256((author/'report.json').read_bytes()).hexdigest(),'host_build':json.loads((out/'supervisor/build.json').read_text()),'result':json.loads((out/'result.json').read_text()),'http_requests':fixture.requests},indent=2))

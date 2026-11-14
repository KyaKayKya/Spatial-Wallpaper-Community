"""Targeted PCM/mixer/output checks. No Unity build or desktop input."""
from pathlib import Path
import subprocess,uuid,json,argparse,shutil
from check_contracts import ROOT,DATA
from windows_framework import references
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--supervisor',type=Path);args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/G/sound'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
files=[ROOT/'tools/sdk/SoundPlaybackChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data/SoundData.cs',ROOT/'Supervisor/Broker/SoundOutput.cs']
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
report={'status':'failed','scope':'sound PCM, bounded voices and user mix only'}
try:
 for name,command in [('compile',command),('checks',[str(out/'Checks.exe')])]:
  result=subprocess.run(command,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
 if args.supervisor:
  supervisor=args.supervisor.resolve();shutil.copyfile(supervisor,out/'Supervisor.exe');shutil.copyfile(supervisor.parent/'Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
  compile_endpoint=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Supervisor.exe'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Endpoint.exe'),str(ROOT/'tools/sdk/SoundEndpointChecks.cs')]
  for name,command in [('endpoint-compile',compile_endpoint),('endpoint',[str(out/'Endpoint.exe'),str(out)])]:
   result=subprocess.run(command,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
 report['status']='passed-targeted-sound-core-not-RC'
finally:(out/'report.json').write_text(json.dumps(report,indent=2))

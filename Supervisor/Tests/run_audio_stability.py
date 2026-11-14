"""Replay the actual author C# with controlled audio/storage, independently of native integration."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,argparse
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
parser=argparse.ArgumentParser();parser.add_argument('--negative-no-latch',action='store_true');args=parser.parse_args()
out=root/'Validation/Content/sdk-runtime/E/audio-stability'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
files=[root/'Supervisor/Tests/AudioStabilityChecks.cs',root/'tools/sdk/experiments/StageEAudioAuthor.cs']
if args.negative_no_latch:
 source=files[1].read_text();assert source.count('returnedAudio=true;')==1
 mutant=out/'StageEAudioAuthor.cs';mutant.write_text(source.replace('returnedAudio=true;','returnedAudio=false;'),encoding='utf-8');files[1]=mutant
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
run=subprocess.run([str(out/'Checks.exe')],capture_output=True,text=True);assert receipt==sources()
report={'status':'partial-controlled-author-replay' if run.returncode==0 else 'failed','sources':receipt,'exitCode':run.returncode,'stdout':run.stdout,'stderr':run.stderr,'cases':14,'scope':'actual author C# with controlled audio/storage; not VM/native audio','sha256':{p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
report['negativeNoLatch']=args.negative_no_latch
(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out);print(run.stdout);run.check_returncode()

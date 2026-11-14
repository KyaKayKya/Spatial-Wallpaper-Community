"""Convert an original C# capability author in a clean SDK-only Unity project."""
import os,subprocess,uuid,json,hashlib,argparse
from build_execution import ROOT,UNITY,create
from provenance import sources
parser=argparse.ArgumentParser();parser.add_argument('--play',action='store_true');args=parser.parse_args()
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/storage-author'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('e'+run[:10]);create(project,[('StageEStorageAuthor.cs',False),('StageEStorageExport.cs',True),('StageEStoragePlay.cs',True)])
receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
if args.play:
 subprocess.run(['py','-3',str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
 env.update(SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'))
result=subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageEStorageExport.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900)
print(out,flush=True);result.check_returncode();author=json.loads((out/'author.json').read_text(encoding='utf-8-sig'));assert author['run_id']==run
if args.play:
 result=subprocess.run([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageEStoragePlay.Run','-logFile',str(out/'play.log')],env=env,timeout=120);result.check_returncode()
 play=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert len(play)==2 and all(p['run_id']==run and p['phase']==5 and p['result']=='author-written' for p in play)
 assert [p['version'] for p in play]==['1','3'],'ordinary Play persistence did not survive restart'
 crash=json.loads((out/'play-crash.json').read_text(encoding='utf-8-sig'));assert crash=={'run_id':run,'state':5,'error':'Closed','released':True,'new_request':0}
assert sources()==receipt,'author conversion source drift'
(out/'report.json').write_text(json.dumps({'status':'E-author-CSharp-converted-and-ordinary-Play' if args.play else 'E-author-CSharp-converted-only','play':play if args.play else None,'broker_crash':crash if args.play else None,'sources':receipt,'author':author,'package_sha256':hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest(),'project':str(project)},indent=2))

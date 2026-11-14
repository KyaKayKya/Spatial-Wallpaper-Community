"""Convert and run original C# LLM author against a real local HTTP service."""
import os,subprocess,uuid,json,hashlib,sys
from build_execution import ROOT,UNITY,create
from provenance import sources
from llm_fixture import LlmFixture
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/llm-author'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('l'+run[:10]);create(project,[('StageELlmAuthor.cs',False),('StageELlmExport.cs',True),('StageELlmPlay.cs',True)])
receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));print(out,flush=True)
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageELlmExport.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True)
fixture=LlmFixture()
try:
 (out/'llm.json').write_text(json.dumps(fixture.config));env.update(SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'),SDK_PLAY_LLM_CONFIG=str(out/'llm.json'))
 subprocess.run([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageELlmPlay.Run','-logFile',str(out/'play.log')],env=env,timeout=120,check=True)
 play=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert play=={'run_id':run,'phase':8,'result':'reply-as-data','cancellation':'Cancelled'}
 assert fixture.requests and all(r['test_auth_matches'] and r['body']['model']=='fixture-model' for r in fixture.requests)
finally:fixture.close()
assert sources()==receipt,'LLM author source drift';author=json.loads((out/'author.json').read_text(encoding='utf-8-sig'));assert author['run_id']==run
(out/'report.json').write_text(json.dumps({'status':'partial-LLM-CSharp-Play-only','sources':receipt,'author':author,'play':play,'http_requests':fixture.requests,'package_sha256':hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest()},indent=2))

"""Compile string value equality and reject a non-string same-name method."""
import os,subprocess,uuid,json,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/string-equality'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('s'+run[:10]);create(project,[('StageStringEqualityAuthor.cs',False),('StageStringEqualityChecks.cs',True)])
(project/'Assets/SdkStringAuthor.asmdef').write_text(json.dumps({'name':'SdkStringAuthor','references':['SpatialWallpaper.Sdk']}),encoding='utf-8')
(project/'Assets/Editor/SdkStringChecks.asmdef').write_text(json.dumps({'name':'SdkStringChecks','references':['SdkStringAuthor','SpatialWallpaper.Sdk','SpatialWallpaper.Sdk.Editor','Cilbox','Unity.Newtonsoft.Json'],'includePlatforms':['Editor']}),encoding='utf-8')
negative=project/'Assets/StringNegative';negative.mkdir()
(negative/'Negative.cs').write_text('using UnityEngine;using SpatialWallpaper.Sdk;public static class StringLookalike{public static bool op_Equality(string a,string b){return true;}}[WallpaperScript]public sealed class StringNegativeAuthor:MonoBehaviour{public bool value;void Start(){value=StringLookalike.op_Equality("a","b");}}',encoding='utf-8')
(negative/'SdkStringNegative.asmdef').write_text(json.dumps({'name':'SdkStringNegative','references':['SpatialWallpaper.Sdk'],'autoReferenced':False}),encoding='utf-8')
env=os.environ.copy();env.update(SDK_STRING_OUTPUT=str(out),SDK_STRING_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
receipt=sources();print(out,flush=True);report={'status':'failed','sources':receipt}
try:
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageStringEqualityChecks.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True,creationflags=subprocess.CREATE_NO_WINDOW)
 result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run and len(result['cases'])==6 and result['distinct_equal_instances'] and result['lookalike_rejected'] and len(result['signature_negatives'])==4
 assert sources()==receipt,'string equality source drift'
 report.update(status='real-string-equality-regression',result=result,packages={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*.spatial')})
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

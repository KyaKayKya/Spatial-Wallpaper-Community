from pathlib import Path
import json,hashlib,argparse
root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description="Generate the existing seven-input-member native QA program; no art or new author API.")
parser.add_argument('--output',type=Path,required=True);parser.add_argument('--fault-on-secondary',action='store_true');args=parser.parse_args();output=args.output.resolve()
assert not output.exists(),"input fixture output must be new"
output.parent.mkdir(parents=True,exist_ok=True)
rules=json.loads((root/'tools/sdk/rules/native-scene-v1.json').read_text())
legacy=json.loads((root/'tools/sdk/rules/v1.json').read_text())
definition={"format":"spatial-native-script","version":1,"sdk":rules["sdk_target"],"vm":legacy["vm"],"command_abi":rules["abi"],"catalog_digest":hashlib.sha256(json.dumps(rules,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode()).hexdigest(),
 "program":{"classes":[{"id":"Demo","fields":[{"id":"target","type":"node"},{"id":"counter","type":"i32"}],"methods":[{"id":"Update","returns":"void","parameters":[],"locals":[],"body":[]}]}]},
 "content":{"frame":[.6,.3375,.5],"resources":[],"nodes":[{"id":"n0","parent":-1,"components":[{"kind":"transform","config":{"position":[0,0,0],"rotation":[0,0,0,1],"scale":[1,1,1]},"fields":{},"callbacks":[]},{"kind":"script","config":{"class":"Demo"},"fields":{"target":0,"counter":0},"callbacks":["Update"]}]}],"prefabs":[]}}
body=[]
def emit(op,value=None):body.append({'op':op,'value':value});return len(body)-1
def call(name,signature='()->System.Single'):emit('call','SpatialWallpaper.Sdk::Input::'+name+signature)
def button(name,kind):emit('ldstr',name);call(kind,'(System.String)->System.Boolean')
def position(code,wheel=False):
 emit('ldarg',0);emit('ldfield','target');emit('ldc.r4',float(code))
 if wheel:call('GetWheel');emit('ldc.r4',0.0)
 else:call('GetPointerX');call('GetPointerY')
 emit('newvector3');emit('call','UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void');emit('ret')
call('IsFocused','()->System.Boolean');unfocused=emit('brfalse',0)
if args.fault_on_secondary:
 button('Secondary','GetButtonDown');skip_fault=emit('brfalse',0);loop=emit('br',0);body[loop]['value']=loop;body[skip_fault]['value']=len(body)
for i,name in enumerate(['Primary','Secondary','Space','Left','Right','Up','Down']):
 for up in [False,True]:
  button(name,'GetButtonUp' if up else 'GetButtonDown');skip=emit('brfalse',0)
  # A repeated edge query must remain true; held must match the same event.
  button(name,'GetButtonUp' if up else 'GetButtonDown');invalid1=emit('brfalse',0)
  button(name,'GetButton');invalid2=emit('brtrue' if up else 'brfalse',0)
  position((101 if up else 1)+i)
  end=len(body);body[skip]['value']=end
  invalid=emit('ret');body[invalid1]['value']=invalid;body[invalid2]['value']=invalid
  # The no-edge branch continues to the next member.
  body[skip]['value']=len(body)
call('GetWheel');emit('ldc.r4',0.0);emit('ceq');no_wheel=emit('brtrue',0);position(201,True)
final=emit('ret');body[unfocused]['value']=body[no_wheel]['value']=final
definition['program']['classes'][0]['methods'][0]['body']=body
definition['build_id']=hashlib.sha256(json.dumps(definition,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest()
p=output;p.write_text(json.dumps(definition,separators=(',',':')),encoding='utf-8');print(p,hashlib.sha256(p.read_bytes()).hexdigest())

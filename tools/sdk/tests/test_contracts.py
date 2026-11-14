import sys,unittest,tempfile,subprocess,copy,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from fixtures import *
from check_contracts import compile_checks,OUT,DATA
class Contracts(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  if compile_checks():raise AssertionError('shared C# validator did not compile')
 def check(self,p,c,code=None,extras=None,manifest_edit=None):
  with tempfile.TemporaryDirectory() as tmp:
   path=Path(tmp)/'test.spatial';path.write_bytes(archive(p,c,extras,manifest_edit))
   result=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(OUT/'ContractChecks.exe'),str(path)],capture_output=True,text=True,timeout=10)
   text=result.stdout+result.stderr
   if code is None:
    self.assertEqual(result.returncode,0,text)
    return json.JSONDecoder().raw_decode(result.stdout.lstrip("\ufeff\n\r"))[0]
   else:
    self.assertNotEqual(result.returncode,0,text);self.assertIn(code,text)
 def test_positive_snapshot(self):self.check(*sample())
 def test_static_rotation_quaternion(self):
  for value in [[0,0,0,1],[0,.70710678,0,.70710678],[0,0,0,-1]]:
   p,c=sample();c['nodes'][0]['components'][0]['config']['rotation']=value;self.check(p,c)
  for value in [[0,0,0,0],[1,1,0,0]]:
   p,c=sample();c['nodes'][0]['components'][0]['config']['rotation']=value;self.check(p,c,'rotation-normalization')
  p,c=sample();c['nodes'][0]['components'][0]['config']['rotation']=[0,0,0];self.check(p,c,'InvalidDataException')
  p,c=sample();c['nodes'][0]['components'][0]['config']['rotation']=[0,0,0,1.01];self.check(p,c,'number-range')
 def test_versions_and_author_labels(self):
  for key in ['schema','sdk','vm','rules','rules_sha256','engine','pipeline','backend','platform']:
   with self.subTest(key=key):
    self.check(*sample(),code='InvalidDataException',manifest_edit=lambda m,k=key:m.update({k:99 if k=='schema' else 'unknown'}))
  self.check(*sample(),code='unexpected-or-missing-fields',manifest_edit=lambda m:m.update(capabilities=['forged']))
 def test_il_failures(self):
  for body,code in [
   ([ins('call','System.IO::ReadAllText')],'member-not-allowed'),
   ([ins('AddComponent','Camera')],'opcode-not-allowed'),
   ([ins('br',999)],'number-range'),
   ([ins('pop'),ins('ret')],'stack-underflow'),
   ([ins('ldc.i4',1),ins('ret')],'return-stack'),
   ([ins('ldc.i4',1),ins('ldc.r4',1),ins('add'),ins('pop'),ins('ret')],'arithmetic-type'),
   ([ins('ret'),ins('nop')],'unreachable-instructions'),
   ([ins('ldc.i4',1),ins('brtrue',3),ins('ldc.i4',1),ins('ret')],'incompatible-stack-join'),
   ([ins('ldarg',1),ins('pop'),ins('ret')],'number-range'),
   ([ins('ldarg',0),ins('ldfield','privateCamera'),ins('pop'),ins('ret')],'field-not-declared'),
   ([ins('ldc.r4',1e300),ins('pop'),ins('ret')],'number-range'),
  ]:
   with self.subTest(code=code):
    p,c=sample();p['classes'][0]['methods'][0]['body']=body;self.check(p,c,code)
 def test_unknown_graph_config_callback(self):
  p,c=sample();c['nodes'][0]['components'][0]['config']['secret']=True;self.check(p,c,'unexpected-or-missing-fields')
  p,c=sample();c['nodes'][0]['components'][0]['kind']='Camera';self.check(p,c,'component-not-allowed')
  p,c=sample();c['nodes'][0]['components'][1]['callbacks']=['OnApplicationFocus'];self.check(p,c,'callback-not-allowed')
  p,c=sample();c['nodes'][0]['components'][1]['callbacks']=[];self.check(p,c,'unregistered-callback')
  p,c=sample();c['nodes'][0]['components'][1]['fields']['target']=1;self.check(p,c,'number-range')
  p,c=sample();p['classes'][0]['fields'][0]['type']='UnityEngine.Camera';self.check(p,c,'type-not-allowed')
 def test_instruction_catalog(self):
  l=lambda op,val=None:ins(op,val)
  scalar=lambda typ:l('ldc.i4',2) if typ=='i32' else l('ldc.r4',2.)
  bodies={
   'newlist':[l('ldc.i4',0),l('newlist','list_i32'),l('pop'),l('ret')],
   'nop':[l('nop'),l('ret')],'ldarg':[l('ldarg',0),l('pop'),l('ret')],
   'ldloc':[l('ldloc',0),l('pop'),l('ret')],'stloc':[l('ldc.i4',2),l('stloc',0),l('ret')],
   'ldfield':[l('ldarg',0),l('ldfield','counter'),l('pop'),l('ret')],
   'stfield':[l('ldarg',0),l('ldc.i4',2),l('stfield','counter'),l('ret')],
   'ldc.i4':[l('ldc.i4',2),l('pop'),l('ret')],'ldc.r4':[l('ldc.r4',2.),l('pop'),l('ret')],
   'ldstr':[l('ldstr','hello'),l('pop'),l('ret')],'ldnull':[l('ldnull'),l('pop'),l('ret')],
   'dup':[l('ldc.i4',2),l('dup'),l('pop'),l('pop'),l('ret')],
   'pop':[l('ldc.i4',2),l('pop'),l('ret')],'br':[l('br',0)],
   'brtrue':[l('ldc.i4',1),l('brtrue',2),l('ret')],'brfalse':[l('ldc.i4',0),l('brfalse',2),l('ret')],
   'ret':[l('ret')],'newarray':[l('ldc.i4',2),l('newarray','i32'),l('pop'),l('ret')],
   'ldlen':[l('ldc.i4',2),l('newarray','i32'),l('ldlen'),l('pop'),l('ret')],
   'ldelem':[l('ldc.i4',2),l('newarray','i32'),l('ldc.i4',0),l('ldelem','i32'),l('pop'),l('ret')],
   'stelem':[l('ldc.i4',2),l('newarray','i32'),l('ldc.i4',0),l('ldc.i4',1),l('stelem','i32'),l('ret')],
   'newobj':[l('newobj','Demo'),l('pop'),l('ret')],
   'newvector3':[l('ldc.r4',0.)]*3+[l('newvector3'),l('pop'),l('ret')],
   'newcolor':[l('ldc.r4',0.)]*4+[l('newcolor'),l('pop'),l('ret')],
   'invoke':[l('ldarg',0),l('invoke','Demo.Update'),l('ret')]
  }
  for op in ['add','sub','mul','div','rem','ceq','clt','cgt','cle','cge','clt.un','cgt.un','cle.un','cge.un','and','or','xor']:
   bodies[op]=[l('ldc.i4',2),l('ldc.i4',1),l(op),l('pop'),l('ret')]
  for op in ['neg','not','conv.i4','conv.r4','conv.bool']:
   bodies[op]=[l('ldc.i4',2),l(op),l('pop'),l('ret')]
  self.assertEqual(set(bodies),set(RULES['instructions'])-{'call'})
  for op,body in bodies.items():
   with self.subTest(op=op):
    p,c=sample();m=p['classes'][0]['methods'][0];m['locals']=['i32'];m['body']=body;self.check(p,c)
    index=next(i for i,x in enumerate(body) if x['op']==op)
    body[index]['value']={'invalid':True}
    self.check(p,c,'InvalidDataException')
 def test_boolean_type_boundaries(self):
  for body,code in [
   ([ins('ldc.i4',1),ins('conv.bool'),ins('ldc.i4',0),ins('ceq'),ins('pop'),ins('ret')],None),
   ([ins('ldc.i4',1),ins('conv.bool'),ins('ldc.i4',0),ins('conv.bool'),ins('and'),ins('pop'),ins('ret')],None),
   ([ins('ldc.r4',1.),ins('conv.bool'),ins('pop'),ins('ret')],'boolean-conversion'),
   ([ins('ldc.i4',1),ins('conv.bool'),ins('ldc.i4',2),ins('add'),ins('pop'),ins('ret')],'arithmetic-type'),
   ([ins('ldc.i4',1),ins('conv.bool'),ins('ldc.i4',2),ins('and'),ins('pop'),ins('ret')],None),
   ([ins('ldc.i4',1),ins('conv.bool'),ins('ldc.r4',2.),ins('and'),ins('pop'),ins('ret')],'bitwise-type')
  ]:
   p,c=sample();p['classes'][0]['methods'][0]['body']=body;self.check(p,c,code)
 def test_reference_branch_boundaries(self):
  reference_types=['string','node','prefab','material','mesh','texture','light','cubemap','list_i32','list_f32','i32[]','Demo']
  for op in ['brtrue','brfalse']:
   for typ in reference_types+['bool','i32','f32','vector3','color']:
    with self.subTest(op=op,type=typ):
     p,c=sample();method=p['classes'][0]['methods'][0];method['locals']=[typ];method['body']=[ins('ldloc',0),ins(op,3),ins('nop'),ins('ret')]
     self.check(p,c,None if typ in reference_types+['bool','i32'] else 'condition-type')
   p,c=sample();p['classes'][0]['methods'][0]['body']=[ins('ldnull'),ins(op,3),ins('nop'),ins('ret')];self.check(p,c)
  for producer in [ins('ldnull'),ins('ldstr',''),ins('ldarg',0)]:
   p,c=sample();p['classes'][0]['methods'][0]['body']=[producer,ins('ldnull'),ins('cgt.un'),ins('pop'),ins('ret')];self.check(p,c)
  for op in ['cgt.un','clt.un','cgt','clt','cle.un','cge.un']:
   for operands in [[ins('ldnull'),ins('ldstr','x')],[ins('ldstr','x'),ins('ldstr','y')],[ins('ldc.r4',0.),ins('ldnull')],[ins('ldc.i4',0),ins('ldnull')]]:
    p,c=sample();p['classes'][0]['methods'][0]['body']=operands+[ins(op),ins('pop'),ins('ret')];self.check(p,c,'InvalidDataException')
  for producer in [ins('ldnull'),ins('ldstr',''),ins('ldarg',0)]:
   p,c=sample();p['classes'][0]['methods'][0]['body']=[producer,ins('conv.bool'),ins('pop'),ins('ret')];self.check(p,c,'boolean-conversion')
 def test_previous_vm_identity_is_rejected(self):
  for vm in ['cilbox-acfca6b-normalized-1','cilbox-acfca6b-normalized-2']:
   with self.subTest(vm=vm):self.check(*sample(),code='InvalidDataException',manifest_edit=lambda m:m.update(vm=vm))
 def test_retired_capability_signatures_rejected(self):
  for signature in ['Storage::Read(System.String)->System.String','Storage::Write(System.String,System.String)->System.Void','Llm::Submit(System.String)->System.Int32']:
   with self.subTest(signature=signature):
    p,c=sample();p['classes'][0]['methods'][0]['body']=[ins('ldstr','key')]+([ins('ldstr','value')] if 'Write(' in signature else [])+[ins('call','SpatialWallpaper.Sdk::'+signature)]+([] if 'System.Void' in signature else [ins('pop')])+[ins('ret')]
    self.check(p,c,'member-not-allowed')
 def test_previous_capability_rules_digest_is_rejected(self):
  # Exact pre-removal identity and the historical G inventory's older identity.
  for digest in ['9de6b941251fa2f60f5ffc20abaa578bab9aac682cc896aab1736b5ab22f899e','5fd167030f605a3811a176c05fbb6903471176205310adfc4793703a7cc09e2b']:
   with self.subTest(digest=digest):self.check(*sample(),code='incompatible-rules',manifest_edit=lambda m:m.update(rules_sha256=digest))
 def test_begin_capability_exact_signatures_still_admitted(self):
  for name,args in [('Storage::BeginRead',[ins('ldstr','key')]),('Storage::BeginWriteText',[ins('ldstr','key'),ins('ldstr','value'),ins('ldstr','0')]),('Llm::BeginPrompt',[ins('ldstr','hello')]),('Llm::BeginCancel',[ins('ldc.i4',1)])]:
   member=next(m for m in RULES['members'] if '::'+name+'(' in m['id'])
   p,c=sample();p['classes'][0]['methods'][0]['body']=args+[ins('call',member['id']),ins('pop'),ins('ret')]
   self.assertEqual(self.check(p,c)['tags'],['private-storage' if name.startswith('Storage') else 'llm-proxy'])
 def test_each_component_config(self):
  for kind,config in [('transform',{'position':[0,0,0],'rotation':[0,0,0,1],'scale':[1,1,1]}),('light',light_config()),('mesh-renderer',{'mesh':None,'material':None}),('script',{'class':'Demo'})]:
   with self.subTest(kind=kind):
    p,c=sample();node=c['nodes'][0]
    if kind=='transform':comp=node['components'][0]
    elif kind=='script':comp=node['components'][1]
    else:comp=component(kind,config);node['components'].append(comp)
    self.check(p,c);comp['config']['unregistered']=1;self.check(p,c,'unexpected-or-missing-fields')
 def test_light_config_boundaries(self):
  for key,value,reason in [('type',3,'number-range'),('range',0,'number-range'),('spot',180,'number-range'),('innerSpot',31,'light-inner-spot-exceeds-spot'),('shadows',3,'number-range'),('shadowStrength',1.1,'number-range'),('bias',-1,'number-range'),('normalBias',4,'number-range'),('near',0,'number-range'),('usePipelineSettings',1,'InvalidDataException')]:
   with self.subTest(key=key):
    p,c=sample();config=light_config();config[key]=value;c['nodes'][0]['components'].append(component('light',config));self.check(p,c,reason)
  for key in light_config():
   with self.subTest(missing=key):
    p,c=sample();config=light_config();del config[key];c['nodes'][0]['components'].append(component('light',config));self.check(p,c,'unexpected-or-missing-fields')
 def test_presentation_boundaries(self):
  config=presentation_config()
  p,c=sample();c['nodes'][0]['components'].append(component('presentation',config));self.check(p,c)
  for key,value in [('threshold',-1),('intensity',11),('scatter',2),('exposure',11),('saturation',101),('vignette',2),('smoothness',0)]:
   with self.subTest(key=key):
    p,c=sample();config=presentation_config();config[key]=value;c['nodes'][0]['components'].append(component('presentation',config));self.check(p,c,'number-range')
  p,c=sample();config=presentation_config();config['gradient']=True;c['nodes'][0]['components'].append(component('presentation',config));self.check(p,c,'presentation-ambient-conflict')
  p,c=sample();child=node();child['id']='n1';child['parent']=0;child['components'].append(component('presentation',presentation_config()));c['nodes'].append(child);self.check(p,c,'presentation-only-on-scene-root')
  p,c=sample();prefab=node();prefab['components'].append(component('presentation',presentation_config()));c['prefabs']=[{'id':'UnsupportedPresentation','nodes':[prefab]}];self.check(p,c,'presentation-only-on-scene-root')
  p,c=sample();config=presentation_config();config['rendererFeature']='untrusted';c['nodes'][0]['components'].append(component('presentation',config));self.check(p,c,'unexpected-or-missing-fields')
 def test_null_default_fields(self):
  p,c=sample();p['classes'][0]['fields'] += [{'id':'label','type':'string'},{'id':'items','type':'i32[]'}];c['nodes'][0]['components'][1]['fields'].update(label=None,items=None);self.check(p,c)
 def test_texture_receiver_subtype(self):
  import struct
  cube=struct.pack('<10if',0x32545753,1,1,17,1,0,0,6,1,0,0.)+bytes(48)
  name='resources/'+hashlib.sha256(cube).hexdigest()+'.stex'
  p,c=sample();c['resources']=[{'id':'Cube','kind':'texture','file':name}]
  p['classes'][0]['fields'][0]['type']='texture'
  c['nodes'][0]['components'][1]['fields']['target']='Cube'
  p['classes'][0]['methods'][0]['body']=[ins('ldarg',0),ins('ldfield','target'),ins('call','UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void'),ins('ret')]
  self.check(p,c,'texture-subtype',extras={name:cube})
 def test_shader_reference_and_property_closure(self):
  report=json.loads((ROOT/'Validation/Content/sdk-runtime/reports/stage-B-unity.json').read_text(encoding='utf-8'))
  bundle=Path(report['fixture_bundle']).read_bytes();shader_file='resources/'+hashlib.sha256(bundle).hexdigest()+'.shaderbundle'
  for shader,props,code in [('not/present',{},'material-shader'),('SpatialWallpaper/SdkContractProbe',{'_DoesNotExist':{'type':'color','value':[1,1,1,1]}},'material-property')]:
   p,c=sample();material=canonical({'shader':shader,'bundle':'Shader','properties':props});material_file='resources/'+hashlib.sha256(material).hexdigest()+'.json'
   c['resources']=[{'id':'Shader','kind':'shader','file':shader_file},{'id':'Mat','kind':'material','file':material_file}]
   self.check(p,c,code,extras={shader_file:bundle,material_file:material})
 def test_rule_constraints_are_complete(self):
  for member in RULES['members']:
   self.assertEqual(len(member['argument_constraints']),len(member['arguments']))
   self.assertIn('ownership',member['receiver_constraint'])
   self.assertIn('ownership',member['return_constraint'])
   for type,constraint in zip(member['arguments'],member['argument_constraints']):self.assertEqual(type,constraint['type'])
  for config in RULES['components'].values():
   for constraint in config.values():self.assertIn('type',constraint)
 def test_resource_prefab_and_tag_invariants(self):
  import struct
  p,c=sample();mesh=struct.pack('<3i',0x314d5753,3,1)+struct.pack('<54f',*([0.]*54))+struct.pack('<4i',3,0,1,2)
  texture=struct.pack('<10if',0x32545753,1,1,4,0,0,0,1,1,0,0.)+bytes([255,255,255,255])
  material=canonical({'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{'_BaseColor':{'type':'color','value':[1,1,1,1]},'_BaseMap':{'type':'texture','value':'Tex'}}})
  extras={}
  for id,kind,data,ext in [('Mesh','mesh',mesh,'.smesh'),('Tex','texture',texture,'.stex'),('Mat','material',material,'.json')]:
   name='resources/'+hashlib.sha256(data).hexdigest()+ext;extras[name]=data;c['resources'].append({'id':id,'kind':kind,'file':name})
  c['nodes'][0]['components'].append(component('mesh-renderer',{'mesh':'Mesh','material':'Mat'}))
  prefab=node();prefab['components'].append(component('script',{'class':'Demo'},{'target':0,'counter':0},['Update']))
  c['prefabs']=[{'id':'Spawn','nodes':[prefab]}];self.check(p,c,extras=extras)
  prefab['components'][1]['fields']['target']=1;self.check(p,c,'number-range',extras=extras)
  prefab['components'][1]['fields']['target']=0;prefab['components'][0]['config']['scale']=[float('nan'),1,1]
  self.check(p,c,'InvalidDataException',extras=extras)
  p,c=sample();body=[]
  for key in ['GetButtonDown','GetBand','Storage::BeginRead','Llm::BeginPrompt']:
   member=next(m for m in RULES['members'] if key in m['id'])
   body.append(ins('ldc.i4',0) if member['arguments'][0]=='i32' else ins('ldstr','test'));body.extend([ins('call',member['id']),ins('pop')])
  body.append(ins('ret'));p['classes'][0]['methods'][0]['body']=body
  self.assertEqual(self.check(p,c)['tags'],['audio-features','input','llm-proxy','private-storage'])
 def test_view_data_labels_include_unused_prefab_and_material(self):
  p,c=sample();self.assertEqual(self.check(p,c)['tags'],[])
  prefab=node();prefab['components'].append(component('mesh-renderer',{'mesh':None,'material':None}));c['prefabs']=[{'id':'Uninstantiated','nodes':[prefab]}]
  self.assertEqual(self.check(p,c)['tags'],['view-data'])
  p,c=sample();material=canonical({'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{}});name='resources/'+hashlib.sha256(material).hexdigest()+'.json';c['resources']=[{'id':'Mat','kind':'material','file':name}]
  self.assertEqual(self.check(p,c,extras={name:material})['tags'],['view-data'])
 def test_builtin_property_reference(self):
  p,c=sample();material=canonical({'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{'_Missing':{'type':'color','value':[1,1,1,1]}}});name='resources/'+hashlib.sha256(material).hexdigest()+'.json'
  c['resources']=[{'id':'Mat','kind':'material','file':name}];self.check(p,c,'material-property-missing',extras={name:material})
 def test_prefab_requires_one_root(self):
  for nodes in [[],[node(),dict(node(),id='n1')]]:
   p,c=sample();c['prefabs']=[{'id':'Bad','nodes':nodes}];self.check(p,c,'prefab-root')
 def test_shader_texture_dimension(self):
  import struct
  report=json.loads((ROOT/'Validation/Content/sdk-runtime/reports/stage-B-unity.json').read_text(encoding='utf-8'))
  bundle=Path(report['fixture_bundle']).read_bytes();shader_file='resources/'+hashlib.sha256(bundle).hexdigest()+'.shaderbundle'
  cube=struct.pack('<10if',0x32545753,1,1,17,1,0,0,6,1,0,0.)+bytes(48);cube_file='resources/'+hashlib.sha256(cube).hexdigest()+'.stex'
  for prop,code in [('_Environment',None),('_Map','material-texture-dimension')]:
   p,c=sample();material=canonical({'shader':'SpatialWallpaper/SdkContractProbe','bundle':'Shader','properties':{prop:{'type':'texture','value':'Cube'}}});mat_file='resources/'+hashlib.sha256(material).hexdigest()+'.json'
   c['resources']=[{'id':'Mat','kind':'material','file':mat_file},{'id':'Cube','kind':'cubemap','file':cube_file},{'id':'Shader','kind':'shader','file':shader_file}]
   self.check(p,c,code,extras={shader_file:bundle,cube_file:cube,mat_file:material})
 def test_script_and_data_field_references(self):
  p,c=sample();p['classes'].append({'id':'Stats','fields':[{'id':'value','type':'i32'}],'methods':[]})
  p['classes'][0]['fields'] += [{'id':'state','type':'Stats'},{'id':'peer','type':'Demo'}]
  c['nodes'][0]['components'][1]['fields'].update(state={'fields':{'value':3}},peer={'script':1})
  child=node();child['id']='n1';child['parent']=0;child['components'].append(component('script',{'class':'Demo'},{'target':1,'counter':0,'state':{'fields':{'value':4}},'peer':{'script':0}},['Update']));c['nodes'].append(child)
  self.check(p,c)
  c['nodes'][0]['components'][1]['fields']['peer']={'script':2};self.check(p,c,'number-range')
 def test_closed_list_contract(self):
  p,c=sample();p['classes'][0]['fields'].append({'id':'items','type':'list_i32'});c['nodes'][0]['components'][1]['fields']['items']=[1,2,3]
  p['classes'][0]['methods'][0]['body']=[ins('ldarg',0),ins('ldfield','items'),ins('ldc.i4',4),ins('call','mscorlib::System.Collections.Generic.List<System.Int32>::Add(System.Int32)->System.Void'),ins('ret')]
  self.check(p,c)
 def test_reserved_class(self):
  p,c=sample();p['classes'][0]['id']='i32';self.check(p,c,'reserved-class-name')
 def test_payload_and_path(self):
  for name in ['author.dll','resources/author.dll','../program.json','AUX.txt','resources/a:b','CONTENT.JSON']:
   with self.subTest(name=name):self.check(*sample(),code='InvalidDataException',extras={name:b'code'})
 def test_all_members_exact_signature(self):
  def value(type):
   return {'i32':ins('ldc.i4',0),'f32':ins('ldc.r4',0.),'string':ins('ldstr','test'),'bool':None,'vector3':None,'color':None}.get(type,ins('ldnull'))
  for member in RULES['members']:
   with self.subTest(member=member['id']):
    p,c=sample();body=[]
    if member['receiver']:body.append(ins('ldnull'))
    for t in member['arguments']:
     if t=='vector3':body += [ins('ldc.r4',0.)]*3+[ins('newvector3')]
     elif t=='color':body += [ins('ldc.r4',0.)]*4+[ins('newcolor')]
     else:body.append(value(t))
    body.append(ins('call',member['id']))
    if member['returns']!='void':body.append(ins('pop'))
    body.append(ins('ret'));p['classes'][0]['methods'][0]['body']=body
    self.check(p,c)
    body[next(i for i,x in enumerate(body) if x['op']=='call')]['value'] += 'unknown'
    self.check(p,c,'member-not-allowed')
if __name__=='__main__':unittest.main()

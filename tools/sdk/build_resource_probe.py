from pathlib import Path
import os,subprocess,json,uuid,struct,hashlib
from build_execution import create,UNITY,ROOT
from fixtures import sample,archive,canonical,node,component
from provenance import sources
def main():
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/D/unity-resources'/run;out.mkdir(parents=True)
 project=ROOT/'Validation/Sdk'/('d'+run[:10])
 create(project,[('StageDResourceObserver.cs',False),('StageDResourceChecks.cs',True),('StageCPipeline.cs',True)])
 program,content=sample()
 compressed=struct.pack('<10if',0x32545753,4,4,24,1,0,0,1,1,0,0.)+bytes(16)
 compressed_file='resources/'+hashlib.sha256(compressed).hexdigest()+'.stex'
 program['classes'][0]['fields'].append({'id':'compressed','type':'texture'});content['nodes'][0]['components'][1]['fields']['compressed']='Compressed'
 content['resources'].append({'id':'Compressed','kind':'texture','file':compressed_file})
 half=struct.pack('<10if',0x32545753,4,4,17,1,0,0,1,1,0,0.)+bytes(128);half_file='resources/'+hashlib.sha256(half).hexdigest()+'.stex'
 content['resources'].append({'id':'Half','kind':'texture','file':half_file});program['classes'][0]['fields'].append({'id':'half','type':'texture'});content['nodes'][0]['components'][1]['fields']['half']='Half'
 material=canonical({'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{'_BaseColor':{'type':'color','value':[1,1,1,1]}}});material_file='resources/'+hashlib.sha256(material).hexdigest()+'.json'
 content['resources'].append({'id':'Material','kind':'material','file':material_file});program['classes'][0]['fields'].append({'id':'material','type':'material'});content['nodes'][0]['components'][1]['fields']['material']='Material'
 content['nodes'][0]['components'].append(component('mesh-renderer',{'mesh':None,'material':'Material'}))
 fixture=json.loads((ROOT/'Validation/Content/sdk-runtime/reports/stage-B-unity.json').read_text(encoding='utf-8'))
 shader=Path(fixture['fixture_bundle']).read_bytes();shader_file='resources/'+hashlib.sha256(shader).hexdigest()+'.shaderbundle'
 shader_material=canonical({'shader':'SpatialWallpaper/SdkContractProbe','bundle':'Shader','properties':{'_Tint':{'type':'color','value':[.2,.7,.9,1]}}});shader_mat_file='resources/'+hashlib.sha256(shader_material).hexdigest()+'.json'
 content['resources'] += [{'id':'Shader','kind':'shader','file':shader_file},{'id':'ShaderMat','kind':'material','file':shader_mat_file}]
 cycle=node();cycle['components'].append(component('script',{'class':'Demo'},{'target':0,'counter':0,'compressed':'Compressed','half':'Half','material':'Material'},['Update']));content['prefabs'].append({'id':'Cycle','nodes':[cycle]})
 (out/'probe.spatial').write_bytes(archive(program,content,{compressed_file:compressed,material_file:material,half_file:half,shader_file:shader,shader_mat_file:shader_material}));receipt=sources()
 env=os.environ.copy();env.update(SDK_D_OUTPUT=str(out),SDK_D_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 code=subprocess.call([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageDResourceChecks.Run','-logFile',str(out/'unity.log')],env=env,timeout=600)
 if code:raise RuntimeError('real Unity resource probe failed: '+str(out))
 value=json.loads((out/'result.json').read_text(encoding='utf-8-sig'))
 assert value['run_id']==run and value['completed_gpu_readbacks']>=4 and value['native_before']==value['native_after']<value['native_pending']
 assert value['graphics']=='Direct3D11' and value['preallocation_rejected'] and value['released_handle_rejected']
 assert value['partial_native_destroyed'] and value['dynamic_pixel_verified_on_GPU'] and value['pixel_bounds_rejected']
 assert value['unsupported_pixel_format_rejected']
 assert value['material_property_type_rejected']
 assert value['half_pixel_verified_on_GPU'] and value['material_mutations_verified']
 assert value['half_component_range_rejected'] and value['prefab_cycles']==64 and value['remaining_graph_scopes']==value['remaining_callbacks']==1
 assert value['dynamic_mesh_verified_on_GPU'] and value['mesh_atomic_rebind_and_attributes'] and value['mesh_invalid_indices_and_foreign_arrays_rejected']
 assert sources()==receipt,'source changed during resource probe'
 (out/'report.json').write_text(json.dumps({'sources':receipt,'result':value},indent=2));print(json.dumps(value,indent=2));print(out)
 return 0
if __name__=='__main__':raise SystemExit(main())

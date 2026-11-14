"""Fail closed on incomplete, stale or contradictory C observations."""
def require_execution(report,current_sources,run_id):
 def need(condition,code):
  if not condition:raise ValueError(code)
 need(report.get('run_id')==run_id and report.get('sources')==current_sources,'stale-execution-sources')
 conversion=report['conversion']
 need(conversion.get('run_id')==run_id and conversion.get('callback_rejected') is True and conversion.get('rollback_checked') is True and conversion.get('vector_boundary_rejected') is True and conversion.get('null_call_rejected') is True,'missing-conversion-boundary')
 build=report['build'];need(build.get('run_id')==run_id and build.get('result')=='Succeeded' and build.get('errors')==0,'worker-build')
 need(set(report['workers'])=={'full-trust','private-station'},'missing-worker-mode')
 for observed in [report['play'],*report['workers'].values()]:
  need(observed.get('run_id')==run_id,'stale-observation')
  for key,value in dict(counter=63,peer=140,active=True,items=62,frames=60,engine='2022.3.62f3').items():
   need(type(observed.get(key)) is type(value) and observed[key]==value,'behaviour:'+key)
  need(abs(observed.get('angle',0)-60)<.01,'native-transform')
 for observed in report['workers'].values():
  need(observed.get('graphics')=='Direct3D11' and observed.get('exit_code')==0,'worker-runtime')
 frames=[report['play_frame_sha256'],*[v['frame_sha256'] for v in report['workers'].values()]]
 need(all(isinstance(h,str) and len(h)==64 and h==frames[0] for h in frames),'render-drift')
 need(set(report['worker_sources'])=={'Assets/StageCWorker.cs','Assets/StageCVisuals.cs','Assets/Editor/StageCPipeline.cs','Assets/Editor/StageCBuild.cs'},'unexpected-worker-source')
 inventory=report['worker_types']
 need('SpatialWallpaper.Sdk.dll' in inventory and set(inventory['Assembly-CSharp.dll']['types'])=={'<Module>','StageCWorker','StageCVisuals','UnitySourceGeneratedAssemblyMonoScriptTypes_v1','UnitySourceGeneratedAssemblyMonoScriptTypes_v1/MonoScriptData','<PrivateImplementationDetails>','<PrivateImplementationDetails>/__StaticArrayInitTypeSize=37','<PrivateImplementationDetails>/__StaticArrayInitTypeSize=63'},'unexpected-worker-types')

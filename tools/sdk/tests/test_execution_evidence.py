import unittest,copy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from execution_evidence import require_execution
class ExecutionEvidence(unittest.TestCase):
 def valid(self):
  observation=dict(run_id='fresh',counter=63,peer=140,active=True,items=62,frames=60,angle=60.,engine='2022.3.62f3')
  worker=dict(observation,graphics='Direct3D11',exit_code=0,frame_sha256='a'*64)
  return dict(run_id='fresh',sources={'a':'hash'},conversion=dict(run_id='fresh',callback_rejected=True,rollback_checked=True,vector_boundary_rejected=True,null_call_rejected=True),build=dict(run_id='fresh',result='Succeeded',errors=0),play=observation,workers={'full-trust':copy.deepcopy(worker),'private-station':copy.deepcopy(worker)},play_frame_sha256='a'*64,worker_sources={p:'hash' for p in ['Assets/StageCWorker.cs','Assets/StageCVisuals.cs','Assets/Editor/StageCPipeline.cs','Assets/Editor/StageCBuild.cs']},worker_types={'SpatialWallpaper.Sdk.dll':{},'Assembly-CSharp.dll':{'types':['<Module>','StageCWorker','StageCVisuals','UnitySourceGeneratedAssemblyMonoScriptTypes_v1','UnitySourceGeneratedAssemblyMonoScriptTypes_v1/MonoScriptData','<PrivateImplementationDetails>','<PrivateImplementationDetails>/__StaticArrayInitTypeSize=37','<PrivateImplementationDetails>/__StaticArrayInitTypeSize=63']}})
 def test_complete_observations(self):require_execution(self.valid(),{'a':'hash'},'fresh')
 def test_missing_stale_drift_and_author_inputs_fail(self):
  changes=[
   lambda r:r['workers'].pop('private-station'),
   lambda r:r.update(sources={'a':'different'}),
   lambda r:r['play'].update(run_id='old'),
   lambda r:r['conversion'].update(rollback_checked=False),
   lambda r:r['conversion'].update(vector_boundary_rejected=False),
   lambda r:r['conversion'].update(null_call_rejected=False),
   lambda r:r['workers']['private-station'].update(angle=0),
   lambda r:r['workers']['private-station'].update(counter=0),
   lambda r:r['workers']['private-station'].update(graphics='Null'),
   lambda r:r['workers']['private-station'].update(frame_sha256='b'*64),
   lambda r:r['worker_sources'].update({'Assets/StageCAuthor.cs':'hash'}),
   lambda r:r['worker_types']['Assembly-CSharp.dll']['types'].append('StageCAuthor')
  ]
  for change in changes:
   with self.subTest(change=changes.index(change)):
    report=self.valid();change(report)
    with self.assertRaises((ValueError,KeyError)):require_execution(report,{'a':'hash'},'fresh')

 def test_real_pe_inventory_detects_author_assembly(self):
  import tempfile,subprocess,shutil
  from check_contracts import DATA,OUT
  from assembly_inventory import inspect
  mono=str(DATA/'MonoBleedingEdge/bin/mono.exe');csc=str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe')
  with tempfile.TemporaryDirectory() as directory:
   root=Path(directory)
   for name,source in [
    ('Assembly-CSharp','public class StageCWorker{} public class StageCVisuals{} public class UnitySourceGeneratedAssemblyMonoScriptTypes_v1{public struct MonoScriptData{}}'),
    ('SpatialWallpaper.Sdk','namespace SpatialWallpaper.Sdk{public class Marker{}}')
   ]:
    cs=root/(name+'.cs');cs.write_text(source)
    subprocess.run([mono,csc,'-nologo','-target:library','-out:'+str(root/(name+'.dll')),str(cs)],check=True,capture_output=True)
   # Use the actual trusted Worker metadata inventory; test fixture source above lacks Unity compiler internals.
   # Therefore the detector must first reject the incomplete trusted type set too.
   with self.assertRaises(subprocess.CalledProcessError):inspect(root)
   cs=root/'Hostile.cs';cs.write_text('public class StageCAuthor{}')
   subprocess.run([mono,csc,'-nologo','-target:library','-out:'+str(root/'Hostile.dll'),str(cs)],check=True,capture_output=True)
   # Scan Hostile.dll alone to ensure the author-name boundary is exercised before missing-core checks.
   (root/'Assembly-CSharp.dll').unlink();(root/'SpatialWallpaper.Sdk.dll').unlink()
   with self.assertRaises(subprocess.CalledProcessError) as error:inspect(root)
   self.assertIn('author-or-Cilbox-type',error.exception.stderr)

"""Exact trusted fixture type sets, including the pinned compiler callback type."""
from pathlib import Path
import json,shutil,subprocess,sys,unittest,uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from check_contracts import ROOT,DATA,OUT
from assembly_inventory import inspect

class AssemblyInventoryChecks(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.out=ROOT/'Validation/Content/sdk-runtime/D/inventory-checks'/uuid.uuid4().hex;cls.out.mkdir(parents=True)
  cls.mono=str(DATA/'MonoBleedingEdge/bin/mono.exe');cecil=DATA/'MonoBleedingEdge/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll'
  shutil.copyfile(cecil,cls.out/'Mono.Cecil.dll');cls.factory=cls.out/'Fixture.exe'
  subprocess.run([cls.mono,str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-r:'+str(cecil),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(cls.factory),str(ROOT/'tools/sdk/tests/AssemblyInventoryFixture.cs')],check=True,capture_output=True,text=True)
 def check(self,host,mutation='',error=None):
  folder=self.out/(host+'-'+(mutation or 'normal'));subprocess.run([self.mono,str(self.factory),str(folder),host,mutation],check=True,capture_output=True,text=True)
  try:result=inspect(folder,host)
  except subprocess.CalledProcessError as failure:
   self.assertIsNotNone(error,failure.stderr);self.assertIn(error,failure.stderr);return
  self.assertIsNone(error,result);self.assertIn('SpatialWallpaper.Sdk.dll',result)
 def test_exact_stage_c(self):self.check('StageCWorker')
 def test_stage_d_trusted_callback(self):self.check('StageDWorker')
 def test_callback_does_not_allow_other_type_drift(self):
  for host,mutation in [('StageDWorker','missing-callback'),('StageDWorker','extra'),('StageCWorker','unexpected-callback')]:
   with self.subTest(host=host,mutation=mutation):self.check(host,mutation,'unexpected-Worker-type')
 def test_author_and_cilbox_still_rejected(self):
  for mutation in ['author','cilbox']:
   with self.subTest(mutation=mutation):self.check('StageDWorker',mutation,'author-or-Cilbox-type')
 def test_production_cannot_contain_fixture_assembly(self):self.check('SdkWorker',error='production-Worker-must-not-contain-Assembly-CSharp')
if __name__=='__main__':unittest.main()

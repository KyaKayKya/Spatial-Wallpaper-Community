import hashlib,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from verify_delivery import verify_inventory,verify_component,require_rc,verify_client_files,verify_client_inputs,client_runtime_path,collect_notices

class DeliveryInventoryTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  (self.root/'sample.bin').write_bytes(b'actual bytes');self.files={'sample.bin':hashlib.sha256(b'actual bytes').hexdigest()}
 def test_exact_bytes_and_inventory(self):verify_inventory(self.root,self.files)
 def test_corrupt_missing_extra_and_unsafe_names(self):
  for files in [{}, {'../sample.bin':self.files['sample.bin']},{'SAMPLE.bin':self.files['sample.bin']},{'sample.bin':'0'*64}]:
   with self.subTest(files=files),self.assertRaises(ValueError):verify_inventory(self.root,files)
  (self.root/'extra.bin').write_bytes(b'extra')
  with self.assertRaisesRegex(ValueError,'inventory'):verify_inventory(self.root,self.files)
 def test_receipt_identity_and_related_source_changes_refuse_reuse(self):
  receipt={'identity':{'sdk':'1'},'sources':{'Runtime/a.cs':'a','tools/unrelated.py':'old'}}
  verify_component(receipt,{'sdk':'1'},{'Runtime/a.cs':'a'})
  for changed in [{'Runtime/a.cs':'b'},{'Runtime/b.cs':'a'}]:
   with self.assertRaisesRegex(ValueError,'related-input'):verify_component(receipt,{'sdk':'1'},changed)
  with self.assertRaisesRegex(ValueError,'identity'):verify_component(receipt,{'sdk':'2'},{'Runtime/a.cs':'a'})
  with self.assertRaisesRegex(ValueError,'mutated'):verify_component({**receipt,'mutated_sources_sha256':{'a':'b'}},{'sdk':'1'},{'Runtime/a.cs':'a'})
 def test_deleted_related_source_also_prevents_binary_reuse(self):
  with self.assertRaisesRegex(ValueError,'related-input-set'):
   verify_component({'identity':{'sdk':'1'},'sources':{'Runtime/a.cs':'a','Runtime/b.cs':'b'}},{'sdk':'1'},{'Runtime/a.cs':'a'},original_related={'Runtime/a.cs':'a','Runtime/b.cs':'b'})
 def test_development_package_cannot_claim_rc(self):
  with self.assertRaisesRegex(ValueError,'RC-acceptance-incomplete'):require_rc({'stage':'development','rc_acceptance':None})
 def test_client_rejects_files_added_after_original_build(self):
  (self.root/'player').mkdir();(self.root/'manager').mkdir()
  for folder in ['player','manager']:(self.root/folder/'app.exe').write_bytes(b'app')
  files={folder+'/app.exe':hashlib.sha256(b'app').hexdigest() for folder in ['player','manager']}
  verify_client_files(self.root,files)
  (self.root/'manager/unlisted.dll').write_bytes(b'added')
  with self.assertRaisesRegex(ValueError,'inventory'):verify_client_files(self.root,files)
 def test_client_source_additions_refused_but_readme_does_not_force_rebuild(self):
  source=self.root/'Packages/com.spatialwallpaper.sdk/Runtime/A.cs';source.parent.mkdir(parents=True);source.write_text('A')
  readme=source.parents[1]/'README.md';readme.write_text('old')
  inputs={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [source,readme]}
  readme.write_text('new');verify_client_inputs(inputs,self.root)
  source.with_name('B.cs').write_text('B')
  with self.assertRaisesRegex(ValueError,'related-input'):verify_client_inputs(inputs,self.root)
 def test_client_runtime_uses_existing_production_default(self):
  self.assertEqual(client_runtime_path(self.root),self.root/'player/sdk-runtime')
 def test_notices_follow_archived_lock_after_project_is_reused(self):
  project=self.root/'project';archive=self.root/'original-build'
  for root,version in [(project,'2.0.0'),(archive/'dependencies','1.0.0')]:
   lock=root/'Packages/packages-lock.json';lock.parent.mkdir(parents=True);lock.write_text(json.dumps({'dependencies':{'example':{'version':version,'source':'registry'}}}))
  for version in ['1.0.0','2.0.0']:
   license=project/('Library/PackageCache/example@'+version+'/LICENSE.md');license.parent.mkdir(parents=True);license.write_text(version)
  output=self.root/'output';output.mkdir()
  notices=collect_notices(output,{'worker':{'project':str(project),'archive':str(archive)}})
  self.assertTrue((notices/'worker/example@1.0.0/LICENSE.md').is_file());self.assertFalse((notices/'worker/example@2.0.0/LICENSE.md').exists())

if __name__=='__main__':unittest.main()

"""Native inputs are validated before Windows cross-build mutates a project."""
import hashlib
import json
from pathlib import Path
import struct
import plistlib
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'macos'))
from native_inputs import prepare_native_inputs, mach_o_architectures
from package_app import finalize_app

class NativeFixture(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
  self.root=Path(self.temp.name)/'project';self.store=Path(self.temp.name)/'native';self.store.mkdir()
  self.sources={}
  for name in ['SpatialMac.m','SpatialCamera.m']:
   p=self.root/'tools/macos'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(name.encode())
   self.sources['tools/macos/'+name]=hashlib.sha256(p.read_bytes()).hexdigest()
  self.manifest={'schema':1,'sources':self.sources,'files':{}}
  for name in ['libSpatialMac.dylib','libSpatialCamera.dylib']:
   data=struct.pack('>II',0xcafebabe,2)+struct.pack('>IIIII',0x1000007,3,48,32,0)+struct.pack('>IIIII',0x100000c,0,80,32,0)
   data+=struct.pack('<IIIIIIII',0xfeedfacf,0x1000007,3,6,0,0,0,0)+struct.pack('<IIIIIIII',0xfeedfacf,0x100000c,0,6,0,0,0,0)
   (self.store/name).write_bytes(data);self.manifest['files'][name]={'sha256':hashlib.sha256(data).hexdigest(),'architectures':['x86_64','arm64']}
  self.save()
 def save(self):
  (self.store/'native-manifest.json').write_text(json.dumps(self.manifest),encoding='utf8')

class NativeInputsTests(NativeFixture):
 def test_fat_table_with_non_macho_slices_is_rejected(self):
  p=self.store/'libSpatialMac.dylib';data=p.read_bytes();p.write_bytes(data[:48]+b'x'*64)
  with self.assertRaisesRegex(ValueError,'slice'):mach_o_architectures(p)
 def test_truncated_fat_slice_is_rejected(self):
  p=self.store/'libSpatialMac.dylib';p.write_bytes(p.read_bytes()[:-1])
  with self.assertRaisesRegex(ValueError,'slice'):mach_o_architectures(p)
 def test_slice_cpu_must_match_fat_table(self):
  p=self.store/'libSpatialMac.dylib';data=bytearray(p.read_bytes());struct.pack_into('<I',data,84,0x1000007);p.write_bytes(data)
  with self.assertRaisesRegex(ValueError,'CPU'):mach_o_architectures(p)
 def test_matching_universal_libraries_are_staged_for_unity_and_tracker(self):
  result=prepare_native_inputs(self.root,self.store)
  self.assertEqual(set(result['files']),{'libSpatialMac.dylib','libSpatialCamera.dylib'})
  self.assertEqual((self.root/'Prototype/Assets/Plugins/macOS/libSpatialMac.dylib').read_bytes(),(self.store/'libSpatialMac.dylib').read_bytes())
  self.assertEqual((self.root/'Validation/Desktop/native/libSpatialCamera.dylib').read_bytes(),(self.store/'libSpatialCamera.dylib').read_bytes())
 def test_changed_native_source_is_rejected_before_staging(self):
  (self.root/'tools/macos/SpatialCamera.m').write_bytes(b'changed ABI')
  with self.assertRaisesRegex(ValueError,'source'):prepare_native_inputs(self.root,self.store)
  self.assertFalse((self.root/'Prototype/Assets/Plugins/macOS/libSpatialMac.dylib').exists())
 def test_corrupted_library_is_rejected_before_any_copy(self):
  (self.store/'libSpatialCamera.dylib').write_bytes(b'corrupt')
  with self.assertRaisesRegex(ValueError,'hash'):prepare_native_inputs(self.root,self.store)
  self.assertFalse((self.root/'Prototype/Assets/Plugins/macOS/libSpatialMac.dylib').exists())
 def test_claimed_universal_but_thin_library_is_rejected(self):
  p=self.store/'libSpatialCamera.dylib';p.write_bytes(struct.pack('<II',0xfeedfacf,0x100000c)+b'\0'*24)
  self.manifest['files'][p.name]['sha256']=hashlib.sha256(p.read_bytes()).hexdigest();self.save()
  with self.assertRaisesRegex(ValueError,'architectur'):prepare_native_inputs(self.root,self.store)
 def test_missing_manifest_is_rejected(self):
  (self.store/'native-manifest.json').unlink()
  with self.assertRaises(FileNotFoundError):prepare_native_inputs(self.root,self.store)

class MacAppTests(NativeFixture):
 def make_app(self):
  prepare_native_inputs(self.root,self.store)
  app=self.root/'Build/SpatialWallpaper.app';contents=app/'Contents'
  (contents/'MacOS').mkdir(parents=True);(contents/'Plugins').mkdir()
  data=(self.store/'libSpatialMac.dylib').read_bytes()
  (contents/'MacOS/Spatial Wallpaper').write_bytes(data);(contents/'Plugins/libSpatialMac.dylib').write_bytes(data)
  (contents/'Info.plist').write_bytes(plistlib.dumps({'CFBundleExecutable':'Spatial Wallpaper','CFBundleIdentifier':'com.example.wallpaper'},fmt=plistlib.FMT_BINARY))
  (self.root/'tools/macos/PrivacyInfo.xcprivacy').write_bytes(b'privacy')
  return app
 def test_app_finalization_is_portable_and_preserves_bundle_identity(self):
  app=self.make_app();result=finalize_app(self.root,app)
  info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
  self.assertEqual(info['CFBundleIdentifier'],'com.example.wallpaper')
  self.assertIn('NSCameraUsageDescription',info)
  self.assertEqual((app/'Contents/Frameworks/libSpatialCamera.dylib').read_bytes(),(self.store/'libSpatialCamera.dylib').read_bytes())
  self.assertEqual((app/'Contents/Resources/PrivacyInfo.xcprivacy').read_bytes(),b'privacy')
  self.assertTrue(result['requires_macos_signing'])
  self.assertEqual(set(result['architectures']['Contents/MacOS/Spatial Wallpaper']),{'arm64','x86_64'})
 def test_missing_player_is_not_treated_as_build_success(self):
  app=self.make_app();(app/'Contents/MacOS/Spatial Wallpaper').unlink()
  with self.assertRaises(FileNotFoundError):finalize_app(self.root,app)
 def test_missing_unity_plugin_is_rejected(self):
  app=self.make_app();(app/'Contents/Plugins/libSpatialMac.dylib').unlink()
  with self.assertRaisesRegex(ValueError,'libSpatialMac'):finalize_app(self.root,app)

if __name__=='__main__':unittest.main()

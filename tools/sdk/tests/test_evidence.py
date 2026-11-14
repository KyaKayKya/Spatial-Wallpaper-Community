import hashlib
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import evidence
def render():
 return {mode:{'exit_code':0,'frames':60,'interpreted_count':61,'graphics':'Direct3D11','width':640,'height':360,'png_valid':True,'frame_sha256':'a'*64} for mode in ['full-trust','appcontainer','private-station']}
def permissions():
 good={'exit_code':0,'app_container':1,'outside_file_read':False,'loopback_connect':False,'spawn_child':False,'foreign_process_access':False,'inherited_parent_secret':False,'input_desktop_opened':False,'input_desktop_attached':False,'escaped_station':False,'f24_marker':False,'f24_after_switch':False}
 control=dict(good,app_container=0,outside_file_read=True,loopback_connect=True,spawn_child=True,foreign_process_access=True,inherited_parent_secret=True,f24_marker=True)
 return {'full-trust':control,'interactive-desktop':good.copy(),'private-desktop':good.copy()}
class EvidenceTests(unittest.TestCase):
 def test_valid_observations_accepted(self):
  evidence.require_render(render());evidence.require_permissions(permissions())
 def test_input_visible_or_input_desktop_opened_must_fail(self):
  for key in ['f24_marker','f24_after_switch','input_desktop_opened','input_desktop_attached','escaped_station']:
   cases=permissions();cases['private-desktop'][key]=True
   with self.subTest(key=key),self.assertRaises(ValueError):evidence.require_permissions(cases)
 def test_render_failure_or_difference_must_fail(self):
  for key,value in [('exit_code',20),('graphics','Null'),('interpreted_count',0),('png_valid',False),('frame_sha256','b'*64),('width',1)]:
   cases=render();cases['private-station'][key]=value
   with self.subTest(key=key),self.assertRaises(ValueError):evidence.require_render(cases)
  cases=render();del cases['private-station']['frame_sha256']
  with self.assertRaises(ValueError):evidence.require_render(cases)
 def test_missing_control_or_mode_cannot_pass(self):
  cases=permissions();del cases['full-trust']
  with self.assertRaises(ValueError):evidence.require_permissions(cases)
  cases=render();del cases['private-station']
  with self.assertRaises(ValueError):evidence.require_render(cases)
 def test_old_or_missing_build_marker_cannot_pass(self):
  for data in [{'result':'Succeeded','errors':0},{'result':'Succeeded','errors':0,'run_id':'old'},{'result':'Succeeded','errors':1,'run_id':'current'}]:
   with self.subTest(data=data),self.assertRaises(ValueError):evidence.require_build_result(data,'current')
  evidence.require_build_result({'result':'Succeeded','errors':0,'run_id':'current'},'current')
 def test_missing_changed_or_extra_source_cannot_match_fixed_lock(self):
  expected={'Cilbox.cs':hashlib.sha256(b'pinned').hexdigest(),'LICENSE':hashlib.sha256(b'MIT').hexdigest()}
  correct={'Cilbox.cs':b'pinned','LICENSE':b'MIT'}
  evidence.require_source_bytes(expected,correct)
  for data in [{'Cilbox.cs':b'pinned'},dict(correct,**{'Cilbox.cs':b'changed'}),dict(correct,extra=b'x')]:
   with self.subTest(data=data),self.assertRaises(ValueError):evidence.require_source_bytes(expected,data)
if __name__=='__main__':unittest.main()

import sys,unittest,copy
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import evidence
class ProductionPermissions(unittest.TestCase):
 def case(self):
  names=['outside_file_read','loopback_connect','spawn_child','input_desktop_opened','input_desktop_attached','f24_marker','escaped_station','foreign_process_access','inherited_parent_secret']
  control={k:True for k in names};control.update(app_container=0,f24_after_switch=False)
  restricted={k:False for k in names};restricted.update(app_container=1,f24_after_switch=False)
  return control,restricted
 def test_valid_control_does_not_assume_key_state_survives_desktop_attach(self):evidence.require_production_permissions(*self.case())
 def test_each_escape_and_missing_control_fails(self):
  control,restricted=self.case()
  for key in restricted:
   if key=='app_container':continue
   bad=copy.deepcopy(restricted);bad[key]=True
   with self.assertRaises(ValueError):evidence.require_production_permissions(control,bad)
  for key in control:
   if key in ['app_container','f24_after_switch']:continue
   bad=copy.deepcopy(control);bad[key]=False
   with self.assertRaises(ValueError):evidence.require_production_permissions(bad,restricted)

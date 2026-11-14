from pathlib import Path
import json,sys,unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from run_performance import cases,require_sampling_owner

class PerformanceMatrix(unittest.TestCase):
 def test_all_frozen_combinations_have_full_windows_and_repetitions(self):
  policy=json.loads((Path(__file__).resolve().parents[1]/'release-policy.json').read_text());matrix=cases(policy)
  self.assertEqual(len(matrix),12)
  self.assertEqual({(c['width'],c['height'],c['fps'],c['repetition']) for c in matrix},{(w,h,f,r) for w,h in [(1280,720),(1920,1080)] for f in [30,60] for r in [1,2,3]})
  self.assertTrue(all(c['warmup_seconds']==60 and c['sample_seconds']==300 for c in matrix))
 def test_outer_cleanup_of_unexpected_child_is_not_normal_completion(self):
  tree={'assignment_complete':True,'handshake_released':True,'active_after_termination':0,'root_kernel_exited':True,'unexpected_remaining_members':False,'cleanup_errors':[]}
  require_sampling_owner(0,tree)
  with self.assertRaisesRegex(ValueError,'owner'):
   require_sampling_owner(0,{**tree,'unexpected_remaining_members':True})

if __name__=='__main__':unittest.main()

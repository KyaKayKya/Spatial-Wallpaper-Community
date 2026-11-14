"""F/G must expose the installed-candidate case catalog without running it."""
from pathlib import Path
import json,subprocess,sys,tempfile,unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/sdk'))
import verify_fg

class InstalledCandidateEntry(unittest.TestCase):
 def test_stage_F_and_G_list_are_read_only_and_bind_installed_layout(self):
  for stage in ['F','G']:
   with self.subTest(stage=stage):
    done=subprocess.run([sys.executable,str(ROOT/'tools/sdk/verify.py'),'--stage',stage,'--platform','windows','--delivery',str(ROOT/'not-created-candidate'),'--list'],cwd=ROOT,capture_output=True,text=True)
    self.assertEqual(done.returncode,0,done.stderr)
    data=json.loads(done.stdout)
    self.assertEqual(data['status'],'catalog-only-not-stage-acceptance')
    self.assertTrue(data['missing_release_checks'])
    self.assertTrue(data['cases'])
    self.assertTrue(any(str(ROOT/'not-created-candidate/runtime') in ' '.join(row['args']) for row in data['cases']))
    self.assertFalse((ROOT/'not-created-candidate').exists())
 def test_incomplete_release_coverage_does_not_start_default_long_matrix(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);(root/'assembly-report.json').write_text('{}')
   with patch.object(sys,'argv',['verify_fg','--stage','G','--delivery',str(root)]),patch.object(verify_fg,'BASE',root/'evidence'),patch.object(verify_fg,'sources',return_value={}),patch.object(verify_fg,'verify_candidate',return_value={'identity':{}}),patch.object(verify_fg,'execute',side_effect=lambda case,*a,**kw:{'id':case.id,'status':'passed'}) as execute:
    self.assertEqual(verify_fg.main(),1)
    execute.assert_not_called()
    report=json.loads((root/'evidence/reports/stage-G.json').read_text())
    self.assertEqual(report['status'],'blocked-release-coverage')
    self.assertTrue(report['missing_release_checks'])
 def test_selected_success_cannot_complete_stage_and_failure_is_preserved(self):
  catalog=verify_fg.registry('F',ROOT/'candidate')
  first=catalog[0].id
  result=verify_fg.verdict('F',catalog,[{'id':first,'status':'passed'}])
  self.assertIn('complete-case-catalog',result['missing_release_checks'])
  self.assertEqual(result['status'],'blocked-release-coverage')
  failed=verify_fg.verdict('F',catalog,[{'id':first,'status':'failed'}])
  self.assertEqual(failed['status'],'failed');self.assertEqual(failed['failed_cases'],[first])

if __name__=='__main__':unittest.main()

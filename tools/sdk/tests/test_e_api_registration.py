"""The unified E run must execute actual facade/Worker binding closure."""
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_e


class ApiRegistration(unittest.TestCase):
    def test_stop_identity_and_race_regressions_are_required(self):
        by = {case.id: case for case in verify_e.registry()}
        self.assertEqual(set(by['worker-stop'].result_cases),
                         {'normal', 'hang', 'nonzero', 'wrong', 'wrong-instance', 'wrong-generation'})
        self.assertTrue(by['worker-stop-identity-negative'].required['expected_negative'])
        for name in ('final-observation', 'exited-kill', 'unpublished-exit', 'ready-after-stop'):
            self.assertEqual(by['stop-' + name].args, ('--negative-' + name,))

    def test_required_api_binding_case_is_selected(self):
        selected = verify_e.selection(verify_e.registry(), ['check-capability-rules'])
        case = selected[-1]
        self.assertEqual(case.script, 'tools/sdk/check_capability_rules.py')
        self.assertEqual(case.statuses, ('passed-static-capability-rule-closure',))
        self.assertTrue(case.sources_required)
        self.assertEqual(case.folder, 'G/capability-rules')


if __name__ == '__main__':
    unittest.main()

"""A successful build includes natural completion of its owned process tree."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from toolchain_process import run_toolchain
from case_process import CaseProcess, Job


@unittest.skipUnless(os.name == 'nt', 'Windows nested Job')
class ToolchainChecks(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.evidence = {}

    def run_tree(self, delay=.8, root_delay=0, code=0, timeout=5):
        (self.root/'leaf.py').write_text(
            'import time\nfrom pathlib import Path\n'
            f'time.sleep({delay!r})\nPath("leaf-done").write_text("done")\n')
        (self.root/'parent.py').write_text(
            'import subprocess,sys,time\nfrom pathlib import Path\n'
            'Path("started").write_text("yes")\n'
            f'time.sleep({root_delay!r})\n'
            'subprocess.Popen([sys.executable,"leaf.py"])\n'
            f'sys.exit({code})\n')
        with (self.root/'log').open('w') as log:
            run_toolchain([sys.executable, str(self.root/'parent.py')],
                          self.root, log, timeout, self.evidence)

    def test_success_waits_for_last_descendant(self):
        self.run_tree()
        self.assertTrue((self.root/'leaf-done').exists())
        self.assertTrue(self.evidence['natural_tree_exit'])
        self.assertGreater(self.evidence['tree_exit_seconds'], .7)
        self.assertEqual(self.evidence['process_tree']['active_before_termination'], 0)

    def test_timeout_cannot_turn_forced_drain_into_success(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            self.run_tree(delay=60, timeout=.7)
        self.assertFalse(self.evidence['natural_tree_exit'])
        self.assertFalse((self.root/'leaf-done').exists())
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_nonzero_root_is_failure_and_cleans_its_tree(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_tree(delay=.1, code=7)
        self.assertFalse(self.evidence['natural_tree_exit'])
        self.assertEqual(self.evidence['exit_code'], 7)
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_failed_build_does_not_wait_for_resident_helper(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_tree(delay=60, code=7, timeout=1.5)
        self.assertEqual(self.evidence['exit_code'], 7)
        self.assertFalse(self.evidence['natural_tree_exit'])
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_single_deadline_includes_root_runtime(self):
        started = time.monotonic()
        with self.assertRaises(subprocess.TimeoutExpired):
            self.run_tree(root_delay=.7, delay=.7, timeout=1.1)
        self.assertFalse(self.evidence['natural_tree_exit'])
        self.assertLess(time.monotonic()-started, 2)

    def test_assignment_failure_does_not_start_command(self):
        with mock.patch.object(Job, 'assign', side_effect=OSError('assignment')):
            with self.assertRaises(OSError):
                self.run_tree()
        self.assertFalse((self.root/'started').exists())
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_handshake_failure_does_not_start_command(self):
        with mock.patch.object(CaseProcess, 'release', side_effect=OSError('handshake')):
            with self.assertRaises(OSError):
                self.run_tree()
        self.assertFalse((self.root/'started').exists())
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_interrupt_during_tree_wait_cleans_owned_descendants(self):
        original = Job.active
        def interrupt(job):
            if (self.root/'started').exists():
                # Interrupt just once; cleanup must still query the Job.
                patcher.stop()
                raise KeyboardInterrupt('controlled cancellation')
            return original(job)
        patcher = mock.patch.object(Job, 'active', interrupt)
        patcher.start()
        try:
            with self.assertRaises(KeyboardInterrupt):
                self.run_tree(delay=60)
        finally:
            patcher.stop()
        self.assertFalse(self.evidence['natural_tree_exit'])
        self.assertEqual(self.evidence['process_tree']['active_after_termination'], 0)

    def test_unproven_cleanup_cannot_return_success(self):
        original = CaseProcess.close
        def close(owner):
            original(owner)
            raise RuntimeError('controlled unproven cleanup')
        with mock.patch.object(CaseProcess, 'close', close):
            with self.assertRaisesRegex(RuntimeError, 'unproven cleanup'):
                self.run_tree(delay=.1)
        self.assertIn('unproven cleanup', self.evidence['cleanup_error'])


if __name__ == '__main__':
    unittest.main()

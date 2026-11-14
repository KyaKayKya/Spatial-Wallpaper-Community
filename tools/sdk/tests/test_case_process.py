"""Real background process-tree ownership; never opens Unity or desktop input."""
import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

SDK = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SDK))
import verify_e


@unittest.skipUnless(os.name == 'nt', 'Windows Job ownership contract')
class CaseProcessChecks(unittest.TestCase):
    def setUp(self):
        self.assertTrue(hasattr(verify_e, 'CaseProcess'), 'case execution must use parent-owned Job')
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.out = self.root/'logs'; self.out.mkdir()
        self.receipt = {'fixture': 'source'}
        self.handles = []
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.kernel.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
        self.kernel.OpenProcess.restype = ctypes.c_void_p
        self.kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        self.kernel.WaitForSingleObject.restype = ctypes.c_ulong
        self.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        self.addCleanup(self.close_handles)
        (self.root/'grandchild.py').write_text('from pathlib import Path\nimport os,time\nPath("grandchild.pid").write_text(str(os.getpid()))\ntime.sleep(60)\n')
        (self.root/'child.py').write_text('from pathlib import Path\nimport subprocess,sys,time\nPath("target-entered").write_text("yes")\nsubprocess.Popen([sys.executable,"grandchild.py"])\ntime.sleep(60)\n')

    def close_handles(self):
        for handle in self.handles:
            self.kernel.CloseHandle(handle)

    def observe_grandchild(self):
        deadline = time.monotonic()+5
        marker = self.root/'grandchild.pid'
        while not marker.exists():
            if time.monotonic() >= deadline:
                self.fail('grandchild startup deadline')
            time.sleep(.01)
        handle = self.kernel.OpenProcess(0x100000, False, int(marker.read_text()))
        self.assertTrue(handle)
        self.handles.append(handle)
        self.assertEqual(self.kernel.WaitForSingleObject(handle, 0), 258)
        return handle

    def execute(self):
        case = verify_e.Case('child', 'child.py', timeout=.1)
        with mock.patch.object(verify_e,'ROOT',self.root), mock.patch.object(verify_e,'BASE',self.root), mock.patch.object(verify_e,'sources',return_value=self.receipt):
            return verify_e.execute(case, {}, self.receipt, self.out)

    def test_timeout_proves_grandchild_kernel_exit(self):
        original = verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            return original(owner, timeout)
        with mock.patch.object(verify_e.CaseProcess,'wait',wait):
            row = self.execute()
        self.assertEqual(row['status'],'failed')
        self.assertIn('TimeoutExpired',row['error'])
        self.assertEqual(row['process_tree']['active_after_termination'],0)
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0],0),0)
        self.assertNotIn('cleanup_error',row)

    def test_normal_tree_completion_is_accepted(self):
        (self.root/'grandchild.py').write_text('from pathlib import Path\nimport os,time\nPath("grandchild.pid").write_text(str(os.getpid()))\nwhile not Path("release-grandchild").exists(): time.sleep(.01)\n')
        (self.root/'child.py').write_text('import subprocess,sys\nsubprocess.run([sys.executable,"grandchild.py"],check=True)\n')
        original=verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            (self.root/'release-grandchild').write_text('release')
            return original(owner,5)
        with mock.patch.object(verify_e.CaseProcess,'wait',wait):
            row=self.execute()
        self.assertEqual(row['status'],'passed',row)
        self.assertEqual(row['process_tree']['active_before_termination'],0)
        self.assertEqual(row['process_tree']['active_after_termination'],0)
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0],0),0)

    def test_runner_exit_with_owned_helper_is_cleaned_before_acceptance(self):
        (self.root/'child.py').write_text('from pathlib import Path\nimport subprocess,sys,time\nsubprocess.Popen([sys.executable,"grandchild.py"],creationflags=subprocess.CREATE_NO_WINDOW)\nwhile not Path("release-root").exists(): time.sleep(.01)\n')
        original = verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            (self.root/'release-root').write_text('release')
            return original(owner, 5)
        import case_process
        helpers = {str(Path(sys.executable).resolve()).casefold(): __import__('hashlib').sha256(Path(sys.executable).read_bytes()).hexdigest()}
        console = Path(os.environ['SystemRoot'])/'System32/conhost.exe'
        helpers[str(console).casefold()] = __import__('hashlib').sha256(console.read_bytes()).hexdigest()
        with mock.patch.object(verify_e.CaseProcess,'wait',wait), mock.patch.object(case_process,'toolchain_helpers',return_value=helpers):
            row = self.execute()
        self.assertEqual(row['status'], 'passed', row)
        self.assertGreater(row['process_tree']['active_before_termination'], 0)
        self.assertTrue(row['process_tree']['remaining_job_members'])
        self.assertEqual(row['process_tree']['active_after_termination'], 0)
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0], 0), 0)

    def test_owner_crash_kill_on_close_reaches_grandchild(self):
        # A separate owner holds the only Job handle. Killing that owner proves
        # KILL_ON_JOB_CLOSE independently of the normal close()/terminate path.
        script=self.root/'owner.py'
        script.write_text('import sys,time\nfrom pathlib import Path\nsys.path.insert(0,'+repr(str(SDK))+')\nfrom case_process import CaseProcess\nowner=CaseProcess()\nwith open("owner.log","w") as log:\n owner.start(Path("child.py").resolve(),[],Path.cwd(),log)\n time.sleep(60)\n')
        owner=subprocess.Popen([sys.executable,str(script)],cwd=self.root,creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            handle=self.observe_grandchild()
            owner.kill();owner.wait(5)
            self.assertEqual(self.kernel.WaitForSingleObject(handle,5000),0)
        finally:
            if owner.poll() is None:
                owner.kill();owner.wait(5)

    def test_unknown_survivor_is_failure_even_after_successful_job_cleanup(self):
        (self.root/'child.py').write_text('from pathlib import Path\nimport subprocess,sys,time\nsubprocess.Popen([sys.executable,"grandchild.py"],creationflags=subprocess.CREATE_NO_WINDOW)\nwhile not Path("release-root").exists(): time.sleep(.01)\n')
        original = verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            (self.root/'release-root').write_text('release')
            return original(owner, 5)
        with mock.patch.object(verify_e.CaseProcess,'wait',wait):
            row = self.execute()
        self.assertEqual(row['status'], 'failed', row)
        self.assertIn('case-descendants-survived-runner', row['error'])
        self.assertTrue(row['process_tree']['unexpected_remaining_members'])
        self.assertEqual(row['process_tree']['active_after_termination'], 0)
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0], 0), 0)

    def test_parent_keyboard_interrupt_closes_grandchild(self):
        def interrupt(owner, timeout):
            self.observe_grandchild()
            raise KeyboardInterrupt('controlled parent exception')
        with mock.patch.object(verify_e.CaseProcess,'wait',interrupt):
            row = self.execute()
        self.assertIn('KeyboardInterrupt',row['error'])
        self.assertEqual(row['process_tree']['active_after_termination'],0)
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0],0),0)

    def test_assignment_failure_never_runs_target(self):
        import case_process
        with mock.patch.object(case_process.Job,'assign',side_effect=OSError('controlled assignment failure')):
            row = self.execute()
        self.assertFalse((self.root/'target-entered').exists())
        self.assertIn('assignment failure',row['error'])
        self.assertEqual(row['process_tree']['active_after_termination'],0)
        self.assertFalse(row['process_tree']['handshake_released'])

    def test_handshake_failure_never_runs_target(self):
        with mock.patch.object(verify_e.CaseProcess,'release',side_effect=OSError('controlled handshake failure')):
            row = self.execute()
        self.assertFalse((self.root/'target-entered').exists())
        self.assertIn('handshake failure',row['error'])
        self.assertEqual(row['process_tree']['active_after_termination'],0)

    def test_target_cannot_run_before_assignment(self):
        import case_process
        original = case_process.Job.assign
        def assign(job, process):
            time.sleep(.15)
            self.assertFalse((self.root/'target-entered').exists())
            return original(job, process)
        original_wait = verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            return original_wait(owner, timeout)
        with mock.patch.object(case_process.Job,'assign',assign), mock.patch.object(verify_e.CaseProcess,'wait',wait):
            row = self.execute()
        self.assertTrue(row['process_tree']['handshake_released'])
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0],0),0)

    def test_unproven_cleanup_blocks_next_case(self):
        import case_process
        (self.root/'tools/sdk').mkdir(parents=True)
        (self.root/'tools/sdk/release-policy.json').write_text('{}')
        (self.root/'next.py').write_text('from pathlib import Path\nPath("NEXT-RAN").write_text("bad")')
        cases=[verify_e.Case('child','child.py',timeout=.1),verify_e.Case('next','next.py',resource='desktop')]
        original_wait=verify_e.CaseProcess.wait
        def wait(owner, timeout):
            self.observe_grandchild()
            return original_wait(owner,timeout)
        with mock.patch.object(verify_e,'ROOT',self.root),mock.patch.object(verify_e,'BASE',self.out),mock.patch.object(verify_e,'sources',return_value=self.receipt),mock.patch.object(verify_e,'registry',return_value=cases),mock.patch.object(sys,'argv',['verify_e.py']),mock.patch.object(verify_e.CaseProcess,'wait',wait),mock.patch.object(case_process.Job,'active',side_effect=OSError('controlled accounting failure')):
            self.assertEqual(verify_e.main(),1)
        report=json.loads((self.out/'reports/stage-E.json').read_text())
        self.assertIn('cleanup_error',report['steps'][0])
        self.assertEqual(report['steps'][1]['status'],'blocked')
        self.assertFalse((self.root/'NEXT-RAN').exists())
        self.assertEqual(self.kernel.WaitForSingleObject(self.handles[0],5000),0)


if __name__ == '__main__':
    unittest.main()

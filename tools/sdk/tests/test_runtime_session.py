"""Execute shared C# ownership and production Supervisor composition controls."""
import json
from pathlib import Path
import subprocess
import sys
import unittest
ROOT=Path(__file__).resolve().parents[3]
class RuntimeSessionExecution(unittest.TestCase):
    def run_case(self,script,args,expected):
        result=subprocess.run([sys.executable,str(ROOT/'tools/sdk'/script),*args],cwd=ROOT,capture_output=True,text=True,timeout=45)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        folder=Path(result.stdout.splitlines()[0]);report=json.loads((folder/'report.json').read_text())
        self.assertEqual(report['status'],expected)
        return report
    def test_late_creation_fault_cleanup_and_serial_retry(self):
        self.run_case('check_runtime_session.py',[],'passed-controlled-session-ownership-only')
    def test_early_release_mutations_are_detected(self):
        for mode in ('skip-terminal-wait','release-on-cleanup-failure'):
            with self.subTest(mode=mode):
                report=self.run_case('check_runtime_session.py',['--negative',mode],'expected-session-mutation-rejected')
                self.assertEqual((report['compile_exit'],report['result_exit']),(0,1))
    def test_supervisor_launch_and_old_stop_ownership(self):
        self.run_case('check_supervised_session_owner.py',[],'passed-controlled-supervisor-session-only')
if __name__=='__main__':unittest.main()

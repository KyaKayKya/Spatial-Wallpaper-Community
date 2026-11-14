import subprocess
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from timed_output import ready_line

class ReadyOutputTests(unittest.TestCase):
 def test_silent_child_deadline_and_cleanup(self):
  child=subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)'],stdout=subprocess.PIPE,text=True)
  try:
   with self.assertRaises(TimeoutError):ready_line(child,0.1)
  finally:
   child.terminate();child.wait(timeout=2);child.stdout.close()
  self.assertIsNotNone(child.returncode)
 def test_bounded_real_child_line(self):
  child=subprocess.Popen([sys.executable,'-c','print("ready",flush=True)'],stdout=subprocess.PIPE,text=True)
  try:self.assertEqual(ready_line(child,2),'ready\n')
  finally:child.wait(timeout=2);child.stdout.close()
 def test_oversized_line_refused(self):
  child=subprocess.Popen([sys.executable,'-c','print("x"*2000,flush=True)'],stdout=subprocess.PIPE,text=True)
  try:
   with self.assertRaises(ValueError):ready_line(child,2)
  finally:child.wait(timeout=2);child.stdout.close()

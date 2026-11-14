import os
from pathlib import Path
import subprocess
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
from retained_process_identity import creation_filetime


class RetainedProcessIdentity(unittest.TestCase):
    def test_invalid_handles_rejected_without_pid_lookup(self):
        for value in (None, True, 0, -1):
            with self.assertRaises(ValueError): creation_filetime(value)

    @unittest.skipUnless(os.name == 'nt', 'Windows retained handle ABI')
    def test_original_handle_creation_remains_readable_after_kernel_exit(self):
        child = subprocess.Popen([sys.executable, '-c', 'import sys; sys.stdin.read()'],
                                 stdin=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            before = creation_filetime(int(child._handle))
            child.communicate(timeout=5)
            self.assertEqual(child.returncode, 0)
            self.assertEqual(creation_filetime(int(child._handle)), before)
            self.assertGreater(before, 0)
        finally:
            if child.poll() is None:
                child.kill(); child.wait(5)


if __name__ == '__main__': unittest.main()

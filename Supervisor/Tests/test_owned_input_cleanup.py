"""Emergency release must not act without both an obligation and ownership proof."""
from pathlib import Path
import ctypes
import json
import tempfile
import unittest
from unittest.mock import patch
import owned_input_cleanup


class CleanupChecks(unittest.TestCase):
    def test_zero_obligation_needs_no_pid_reopen_or_release(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / 'input-obligations.bin').write_bytes(b'\0')
            (folder / 'inject-old.request.pid').write_text('123 456')
            with patch.object(ctypes, 'WinDLL', side_effect=AssertionError('no native action required')):
                owned_input_cleanup.cleanup(folder)
            attempt = json.loads((folder / 'external-input-cleanup.json').read_text())['attempts'][-1]
            self.assertEqual(attempt['remaining_obligations'], 0)
            self.assertEqual(attempt['children'], [])
            self.assertEqual(attempt['child_observation'], 'not-required-zero-obligations-unverified')

    def test_nonzero_obligation_access_denied_never_releases(self):
        class Kernel:
            OpenProcess = staticmethod(lambda *args: 0)
            WaitForSingleObject = staticmethod(lambda *args: self.fail('must not wait unknown process'))
            GetProcessTimes = staticmethod(lambda *args: self.fail('must not inspect unknown process'))
            CloseHandle = staticmethod(lambda *args: self.fail('no process handle acquired'))
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / 'input-obligations.bin').write_bytes(b'\2')
            (folder / 'inject-old.request.pid').write_text('123 456')
            with patch.object(ctypes, 'WinDLL', return_value=Kernel()) as load, patch.object(ctypes, 'get_last_error', return_value=5):
                with self.assertRaises(OSError) as caught:
                    owned_input_cleanup.cleanup(folder)
                self.assertEqual(caught.exception.winerror, 5)
                load.assert_called_once_with('kernel32', use_last_error=True)
            self.assertEqual((folder / 'input-obligations.bin').read_bytes(), b'\2')


if __name__ == '__main__':
    unittest.main()

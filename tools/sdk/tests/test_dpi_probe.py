import json
import ctypes
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import sdk_environment as api


class DpiProbe(unittest.TestCase):
    @unittest.skipUnless(sys.platform == 'win32', 'Windows process awareness')
    def test_real_probe_does_not_change_parent_awareness(self):
        library = ctypes.WinDLL('shcore')
        library.GetProcessDpiAwareness.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_int)]
        library.GetProcessDpiAwareness.restype = ctypes.c_long
        before, after = ctypes.c_int(), ctypes.c_int()
        self.assertEqual(library.GetProcessDpiAwareness(None, ctypes.byref(before)), 0)
        result = api.dpi()
        self.assertEqual(library.GetProcessDpiAwareness(None, ctypes.byref(after)), 0)
        self.assertEqual(before.value, after.value)
        self.assertIn(result['status'], ['measured', 'unmeasured'])
        if result['status'] == 'unmeasured': self.assertNotIn('value', result)

    def value(self):
        return {'status': 'measured', 'source': 'isolated per-monitor process',
                'value': [{'rectangle': [0, 0, 1920, 1080], 'effective_dpi': [144, 144]}]}

    def run_value(self, value, returncode=0):
        with patch.object(api.subprocess, 'run', return_value=SimpleNamespace(returncode=returncode, stdout=json.dumps(value))) as run:
            result = api.dpi()
        return result, run

    def test_measured_dpi_uses_isolated_no_window_process(self):
        result, run = self.run_value(self.value())
        self.assertEqual(result['value'][0]['effective_dpi'], [144, 144])
        self.assertIn('dpi_probe.py', str(run.call_args.args[0]))
        self.assertEqual(run.call_args.kwargs['timeout'], 5)
        self.assertEqual(run.call_args.kwargs['creationflags'], subprocess.CREATE_NO_WINDOW)

    def test_timeout_failure_invalid_and_virtualized_value_are_unmeasured(self):
        for error in (subprocess.TimeoutExpired('probe', 5), OSError('not started')):
            with patch.object(api.subprocess, 'run', side_effect=error):
                self.assertEqual(api.dpi()['status'], 'unmeasured')
        for value in [{'status': 'unmeasured', 'reason': 'not aware'}, {},
                      {'status': 'measured', 'source': 'bad', 'value': []}]:
            self.assertEqual(self.run_value(value)[0]['status'], 'unmeasured')
        value = self.value(); value['value'][0]['effective_dpi'] = [True, 96]
        self.assertEqual(self.run_value(value)[0]['status'], 'unmeasured')
        self.assertEqual(self.run_value(self.value(), 1)[0]['status'], 'unmeasured')


if __name__ == '__main__': unittest.main()

import ast
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
from native_exit_trace import capture_exit_trace
from host_exit_trace import MAGIC, CAPACITY, SIZE


class NativeExitTrace(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name); (self.path/'state').mkdir()
        self.trace = self.path/'state/exit.trace'
        data = bytearray(SIZE)
        struct.pack_into('<IIqiiiiqqiiq', data, 0, MAGIC, 1, 2, 123, CAPACITY, 1, 0, 1000, 100, 0, 1, 456)
        struct.pack_into('<qqiiii', data, 64, 1, 101, 18, 6, 0, 0)
        self.data = data; self.trace.write_bytes(data)

    def test_main_returning_never_proves_kernel_exit(self):
        value = capture_exit_trace(self.path, 123, 456, 'before-fallback')
        self.assertEqual(value['trace']['status'], 'stable')
        self.assertEqual(value['trace']['identity_binding'], 'pid-and-creation')
        self.assertTrue(value['trace']['main_returning_observed'])
        self.assertFalse(value['trace']['kernel_exit_proven'])

    def test_unknown_identity_never_downgrades_to_pid_only(self):
        for pid, creation in [(123, None), (123, 0), (123, True), (None, 456)]:
            value = capture_exit_trace(self.path, pid, creation, 'before-fallback')
            self.assertEqual(value['trace']['status'], 'identity-unavailable')
            self.assertFalse(value['trace']['kernel_exit_proven'])

    def test_reused_pid_incomplete_or_missing_trace_preserved(self):
        self.assertEqual(capture_exit_trace(self.path, 123, 457, 'before-fallback')['trace']['status'], 'identity-or-schema-mismatch')
        struct.pack_into('<q', self.data, 8, 3); self.trace.write_bytes(self.data)
        self.assertEqual(capture_exit_trace(self.path, 123, 456, 'before-fallback')['trace']['status'], 'incomplete-publication')
        self.trace.unlink()
        self.assertEqual(capture_exit_trace(self.path, 123, 456, 'after-fallback')['trace']['status'], 'unavailable')

    def test_reader_failure_remains_diagnostic_only(self):
        with patch('native_exit_trace.read_host_exit_trace', side_effect=OSError('mapping failure')):
            value = capture_exit_trace(self.path, 123, 456, 'before-fallback')
        self.assertEqual(value['trace']['status'], 'reader-failed')
        self.assertFalse(value['trace']['kernel_exit_proven'])
        self.assertIn('mapping failure', value['trace']['error'])

    def test_actual_finally_snapshots_bracket_fallback_and_preserve_primary(self):
        source = Path(__file__).resolve().parents[3]/'Supervisor/Tests/run_native_input_host.py'
        tree = ast.parse(source.read_text())
        final = [node for node in tree.body if isinstance(node, ast.Try)][-1].finalbody
        code = compile(ast.fix_missing_locations(ast.Module(body=final, type_ignores=[])), str(source), 'exec')
        test = self
        class Host:
            pid = 123
            def poll(self): return None
            def terminate(self):
                test.assertIn('host_exit_trace_before_fallback', scope['report'])
                test.trace.unlink()
                raise OSError('fallback failed')
        primary = RuntimeError('acceptance failed')
        scope = dict(sys=sys, args=SimpleNamespace(desktop=False, diagnostic_worker=False),
                     host=Host(), host_creation=456, report={}, out=self.path, capture_exit_trace=capture_exit_trace)
        caught = None
        try:
            try: raise primary
            finally: exec(code, scope)
        except Exception as error: caught = error
        self.assertIs(caught, primary)
        self.assertEqual(scope['report']['host_exit_trace_before_fallback']['trace']['status'], 'stable')
        self.assertEqual(scope['report']['host_exit_trace_after_fallback']['trace']['status'], 'unavailable')
        self.assertFalse(scope['report']['host_exited'])
        self.assertIn('fallback failed', scope['report']['host_cleanup_error'])


if __name__ == '__main__': unittest.main()

from pathlib import Path
import ast
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
from failure_report import save_report, failure_receipt
from native_exit_trace import capture_exit_trace


class FailureReport(unittest.TestCase):
    def test_failure_receipt_preserves_identity_and_original_failure(self):
        path = Mock()
        original = RuntimeError('render-profile-observation-capture')
        with self.assertRaises(RuntimeError) as caught:
            with failure_receipt(path, {'worker_build': 'bound', 'sources': {'x':'y'}}):
                raise original
        self.assertIs(caught.exception, original)
        import json
        value = json.loads(path.write_text.call_args.args[0])
        self.assertEqual(value['status'], 'failed')
        self.assertEqual(value['worker_build'], 'bound')
        self.assertIn('render-profile-observation-capture', value['primary_failure'])

    def test_failure_receipt_never_overwrites_success(self):
        path = Mock()
        with failure_receipt(path, {}):
            pass
        path.write_text.assert_not_called()

    def test_audio_finally_keeps_primary_when_report_and_stderr_fail(self):
        source = Path(__file__).resolve().parents[3]/'Supervisor/Tests/run_audio_host.py'
        tree = ast.parse(source.read_text())
        final = [node for node in tree.body if isinstance(node, ast.Try)][-1].finalbody
        code = compile(ast.fix_missing_locations(ast.Module(body=final, type_ignores=[])), str(source), 'exec')
        path = Mock(); path.write_text.side_effect = OSError('audio report disk failure')
        class Output:
            def __truediv__(self, name): return path
        primary = RuntimeError('original audio failure')
        stderr = Mock(); stderr.write.side_effect = OSError('stderr failed')
        import json
        scope = dict(sys=sys, json=json, args=SimpleNamespace(pause_sampler=False), sampler=None,
                     source_job=None, source_child=None, children=[], report={}, out=Output(), save_report=save_report)
        observed = None
        with patch.object(sys, 'stderr', stderr), patch('builtins.print', side_effect=OSError('stdout failed')):
            try:
                try: raise primary
                finally: exec(code, scope)
            except Exception as error: observed = error
        self.assertIs(observed, primary)
        self.assertIn('audio report disk failure', primary.report_save_error)

    def test_actual_runner_finally_keeps_primary_when_report_and_stderr_fail(self):
        source = Path(__file__).resolve().parents[3]/'Supervisor/Tests/run_native_input_host.py'
        tree = ast.parse(source.read_text())
        final = [node for node in tree.body if isinstance(node, ast.Try)][-1].finalbody
        code = compile(ast.fix_missing_locations(ast.Module(body=final, type_ignores=[])), str(source), 'exec')
        path = Mock(); path.write_text.side_effect = OSError('report disk failure')
        class Output:
            def __truediv__(self, name): return path
        primary = RuntimeError('original runtime failure')
        stderr = Mock(); stderr.write.side_effect = OSError('stderr failed')
        scope = dict(sys=sys, args=SimpleNamespace(desktop=False, diagnostic_worker=False),
                     host=None, host_creation=None, report={}, out=Output(), capture_exit_trace=capture_exit_trace)
        observed = None
        with patch.object(sys, 'stderr', stderr):
            try:
                try: raise primary
                finally: exec(code, scope)
            except Exception as error: observed = error
        self.assertIs(observed, primary)
        self.assertEqual(scope['report']['primary_failure'], repr(primary))
        self.assertIn('report disk failure', primary.report_save_error)

    def test_primary_survives_save_and_stderr_failure(self):
        primary = RuntimeError('original stop failure')
        path = Mock(); path.write_text.side_effect = OSError('disk unavailable')
        stderr = Mock(); stderr.write.side_effect = OSError('stderr unavailable')
        observed = None
        try:
            try: raise primary
            finally: save_report(path, {}, primary, stderr)
        except Exception as error: observed = error
        self.assertIs(observed, primary)
        self.assertIn('disk unavailable', primary.report_save_error)

    def test_no_primary_raises_original_save_error_even_if_stderr_fails(self):
        error = OSError('disk unavailable')
        path = Mock(); path.write_text.side_effect = error
        stderr = Mock(); stderr.write.side_effect = OSError('stderr unavailable')
        with self.assertRaises(OSError) as caught: save_report(path, {}, None, stderr)
        self.assertIs(caught.exception, error)

    def test_successful_save_keeps_original_report(self):
        path = Mock(); save_report(path, {'primary_failure': 'x'})
        self.assertIn('primary_failure', path.write_text.call_args.args[0])


if __name__ == '__main__': unittest.main()

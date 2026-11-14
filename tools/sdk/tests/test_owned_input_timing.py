import importlib.util
from pathlib import Path
import sys
import unittest
import ast
import json
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))


class OwnedTiming(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(importlib.util.find_spec('owned_input_timing'), 'owned timing missing')
        from owned_input_timing import ForcedClientTiming
        self.clock = 1.0
        self.timing = ForcedClientTiming(lambda: self.clock)

    def test_real_phase_order_and_separate_durations(self):
        self.timing.begin_force()
        self.clock = 1.025
        self.timing.client_exited()
        self.clock = 1.080
        self.timing.cleanup_finished()
        data = self.timing.data
        self.assertEqual(data['timing_clock'], 'runner-monotonic-ms')
        self.assertEqual(data['forced_started_at_ms'], 1000)
        self.assertEqual(data['forced_to_kernel_exit_ms'], data['client_kernel_exited_at_ms'] - 1000)
        self.assertEqual(data['kernel_exit_to_cleanup_complete_ms'],
                         data['cleanup_completed_at_ms'] - data['client_kernel_exited_at_ms'])
        self.assertTrue(data['cleanup_completed'])
        self.assertNotIn('worker_kernel_exited_at_ms', data)

    def test_kill_or_wait_failure_keeps_unknown_exit_and_cleanup(self):
        self.timing.begin_force()
        self.assertIsNone(self.timing.data['client_kernel_exited_at_ms'])
        self.assertIsNone(self.timing.data['forced_to_kernel_exit_ms'])
        self.assertIsNone(self.timing.data['cleanup_completed_at_ms'])
        self.assertFalse(self.timing.data['cleanup_completed'])
        with self.assertRaises(ValueError):
            self.timing.cleanup_finished()

    def test_cleanup_failure_keeps_known_kernel_exit_but_unknown_cleanup(self):
        self.timing.begin_force()
        self.clock = 2
        self.timing.client_exited()
        self.assertEqual(self.timing.data['forced_to_kernel_exit_ms'], 1000)
        self.assertFalse(self.timing.data['cleanup_completed'])
        self.assertIsNone(self.timing.data['kernel_exit_to_cleanup_complete_ms'])

    def test_cleanup_retry_does_not_reset_original_kill_or_exit(self):
        self.timing.begin_force()
        self.clock = 2
        self.timing.begin_force()
        self.timing.client_exited()
        self.clock = 3
        self.timing.client_exited()
        self.assertEqual(self.timing.data['forced_started_at_ms'], 1000)
        self.assertEqual(self.timing.data['client_kernel_exited_at_ms'], 2000)

    def test_missing_force_and_backwards_clock_are_rejected(self):
        with self.assertRaises(ValueError):
            self.timing.client_exited()
        self.timing.begin_force()
        self.clock = .5
        with self.assertRaises(ValueError):
            self.timing.client_exited()
        self.assertIsNone(self.timing.data['client_kernel_exited_at_ms'])

    def test_actual_runner_finally_preserves_primary_and_cleanup_failures(self):
        # Execute the actual runner's finally block with owned-process/Job stand-ins.
        # No runner imports, native APIs, subprocesses or input operations execute.
        path = Path(__file__).resolve().parents[3]/'Supervisor/Tests/run_owned_input_timeout.py'
        parsed = ast.parse(path.read_text())
        final = next(node.finalbody for node in parsed.body if isinstance(node, ast.Try) and node.finalbody)
        body = ast.parse("timing.begin_force(); child.kill(); child.wait(5); timing.client_exited(); report['client_kernel_exit_observed']=True\nraise RuntimeError('primary-probe')").body
        code = compile(ast.fix_missing_locations(ast.Module(body=[ast.Try(body=body, handlers=[],
                       orelse=[], finalbody=final)], type_ignores=[])), str(path), 'exec')
        for failure in (None, 'kill', 'wait', 'job', 'release'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temp:
                from owned_input_timing import ForcedClientTiming
                events, now = [], [1.0]
                def event(name):
                    events.append(name); now[0] += .125
                    if name == failure:
                        raise RuntimeError('injected-' + name)
                class Child:
                    alive = True
                    def kill(self):
                        event('kill'); self.alive = False
                    def wait(self, timeout):
                        event('wait'); return 1
                    def poll(self):
                        return None if self.alive else 1
                class Outer:
                    def close(self):
                        event('job')
                timing = ForcedClientTiming(lambda: now[0])
                report = {'status': 'failed', 'forced_lifecycle': timing.data}
                scope = dict(timing=timing, child=Child(), outer=Outer(), suspended=None,
                             report=report, out=Path(temp), cleanup=lambda _: event('release'),
                             selected=None, previous=0, sys=sys, json=json)
                expected = 'injected-' + failure if failure in ('kill', 'wait') else 'primary-probe'
                with self.assertRaisesRegex(RuntimeError, expected):
                    exec(code, scope)
                stored = json.loads((Path(temp)/'report.json').read_text())
                self.assertIn(expected, stored['primary_failure'])
                self.assertEqual(stored['status'], 'failed')
                if failure:
                    self.assertFalse(stored['forced_lifecycle']['cleanup_completed'])
                    self.assertIsNone(stored['forced_lifecycle']['cleanup_completed_at_ms'])
                    self.assertTrue(stored['cleanup_errors'])
                else:
                    self.assertTrue(stored['forced_lifecycle']['cleanup_completed'])
                    self.assertLess(events.index('wait'), events.index('job'))
                    self.assertLess(events.index('job'), events.index('release'))


if __name__ == '__main__':
    unittest.main()

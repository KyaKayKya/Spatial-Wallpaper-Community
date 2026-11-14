import copy
import importlib.util
import sys
import unittest
import tempfile
from dataclasses import replace
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


class InputCycles(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(importlib.util.find_spec('input_cycles'), 'cycle gate is missing')
        import input_cycles
        self.gate = input_cycles
        self.policy = {'input': {'start_stop_cycles': 100, 'fault_cycles_each': 20},
                       'runtime': {'normal_stop_grace_ms': 500}}

    def native(self):
        return {'result': {'author_durable_revision': 3, 'stop_exit': True,
                'lifecycle': {'attempt': 'a'*32, 'worker_pid': 42,
                              'worker_creation_filetime': 123456789, 'retained_worker_creation_filetime': 123456789,
                    'worker_started': True, 'worker_kernel_exited': True,
                    'graceful_stop': True, 'forced_stop': False,
                    'stop_reason': 'requested', 'observed_exit_code': 0,
                    'timing_clock': 'host-monotonic-ms', 'cleanup_completed': True,
                    'stop_requested_at_ms': 1, 'stop_published_at_ms': 2,
                    'worker_kernel_exited_at_ms': 500, 'cleanup_completed_at_ms': 550,
                    'forced_started_at_ms': None, 'forced_to_kernel_exit_ms': None,
                    'kernel_exit_to_cleanup_complete_ms': 50,
                    'normal_stop_to_kernel_exit_ms': 499}}}

    def forced_timing(self):
        return {'timing_clock': 'host-monotonic-ms', 'forced_alive_observed': True, 'forced_started_at_ms': 100,
                'worker_kernel_exited_at_ms': 125, 'cleanup_completed_at_ms': 160,
                'forced_to_kernel_exit_ms': 25, 'kernel_exit_to_cleanup_complete_ms': 35,
                'cleanup_completed': True}

    def test_frozen_schedule_includes_each_fault_without_multiplication(self):
        plan = self.gate.schedule(self.policy)
        self.assertEqual(sum(s['kind'] == 'normal' for s in plan), 100)
        faults = [s for s in plan if s['kind'] == 'fault']
        self.gate.require_fault_catalog(self.gate.engine.registry())
        for name in self.gate.FAULT_CASES:
            self.assertEqual(sum(s['case'] == name for s in faults), 20)
        self.assertEqual(len({s['id'] for s in plan}), len(plan))

    def test_graceful_exact_boundary_and_missing_forced_late_negatives(self):
        data = self.native()
        data['result']['lifecycle']['stop_requested_at_ms'] = 0
        data['result']['lifecycle']['normal_stop_to_kernel_exit_ms'] = 500
        self.gate.native_proof(data, self.policy, set())
        for change in ({'forced_stop': True}, {'graceful_stop': False},
                       {'normal_stop_to_kernel_exit_ms': 501},
                       {'normal_stop_to_kernel_exit_ms': float('nan')},
                       {'stop_published_at_ms': 501}, {'cleanup_completed_at_ms': 499},
                       {'kernel_exit_to_cleanup_complete_ms': 49}, {'observed_exit_code': 124},
                       {'worker_kernel_exited': False}, {'worker_pid': True}):
            negative = copy.deepcopy(data)
            negative['result']['lifecycle'].update(change)
            with self.assertRaises(ValueError):
                self.gate.native_proof(negative, self.policy, set())
        del data['result']['lifecycle']
        with self.assertRaises(ValueError):
            self.gate.native_proof(data, self.policy, set())

    def test_same_attempt_cannot_count_twice_and_author_effect_required(self):
        seen = set()
        self.gate.native_proof(self.native(), self.policy, seen)
        with self.assertRaises(ValueError):
            self.gate.native_proof(self.native(), self.policy, seen)
        data = self.native()
        data['result']['author_durable_revision'] = 2
        with self.assertRaises(ValueError):
            self.gate.native_proof(data, self.policy, set())

    def test_continuity_edge_count_keeps_normal_stop_requirements(self):
        data=self.native();data['result']['author_durable_revision']=70
        self.gate.native_proof(data,self.policy,set(),expected_revision=70)
        for expected in (3,69,71,True,70.0,0):
            with self.subTest(expected=expected),self.assertRaises(ValueError):
                self.gate.native_proof(data,self.policy,set(),expected_revision=expected)
        for change in ({'forced_stop':True},{'cleanup_completed':False},{'worker_kernel_exited':False},
                       {'normal_stop_to_kernel_exit_ms':501}):
            bad=copy.deepcopy(data);bad['result']['lifecycle'].update(change)
            with self.subTest(change=change),self.assertRaises(ValueError):
                self.gate.native_proof(bad,self.policy,set(),expected_revision=70)

    def test_first_failure_stops_and_preserves_failed_row(self):
        calls, saved = [], []
        def run(step):
            calls.append(step['id'])
            return {'status': 'failed' if len(calls) == 2 else 'passed', 'proof': step['id']}
        rows = self.gate.run_steps([{'id': str(i)} for i in range(10)], run,
                                   lambda rows: saved.append(copy.deepcopy(rows)))
        self.assertEqual(len(calls), 2)
        self.assertEqual(rows[-1]['proof'], '1')
        self.assertEqual(saved[-1], rows)

    def test_unproven_owner_or_surviving_child_cannot_pass(self):
        good = {'assignment_complete': True, 'handshake_released': True,
                'active_after_termination': 0, 'root_kernel_exited': True,
                'unexpected_remaining_members': False}
        self.gate.owner_proof({'status': 'passed', 'process_tree': good})
        for key, value in [('active_after_termination', 1), ('root_kernel_exited', False),
                           ('unexpected_remaining_members', True), ('assignment_complete', False)]:
            bad = dict(good); bad[key] = value
            with self.assertRaises(ValueError):
                self.gate.owner_proof({'status': 'passed', 'process_tree': bad})

    def test_timeout_requires_real_hold_then_release_and_fresh_injectors(self):
        data = {key: True for key in ('outer_job_assigned', 'client_kernel_exit_observed',
            'without_outer_cleanup_held', 'held_before_kill', 'native_released', 'outer_job_active_zero')}
        data.update(ledger_after_cleanup=0, published_injector_identities=[[123, 456]])
        data['forced_lifecycle'] = self.forced_timing()
        data['forced_lifecycle']['timing_clock'] = 'runner-monotonic-ms'
        data['forced_lifecycle']['client_kernel_exited_at_ms'] = data['forced_lifecycle'].pop('worker_kernel_exited_at_ms')
        seen = set()
        proof = self.gate.timeout_proof(data, seen)
        self.assertTrue(proof['forced_cleanup'])
        self.assertFalse(proof['normal_stop_claimed'])
        with self.assertRaises(ValueError):
            self.gate.timeout_proof(data, seen)
        for key in ('native_released', 'without_outer_cleanup_held', 'client_kernel_exit_observed'):
            bad = dict(data); bad[key] = False
            with self.assertRaises(ValueError):
                self.gate.timeout_proof(bad, set())
        bad = dict(data); bad['published_injector_identities'] = [[123, 456], [123, 456]]
        with self.assertRaises(ValueError):
            self.gate.timeout_proof(bad, set())

    def test_force_cleanup_negative_is_never_normal_cycle(self):
        data = self.native()
        data['result']['cleanup_fault_detected'] = True
        data['result']['lifecycle'].update(graceful_stop=False, forced_stop=True)
        data['result']['lifecycle'].update(self.forced_timing())
        self.gate.native_proof(data, self.policy, set(), cleanup_negative=True)
        with self.assertRaises(ValueError):
            self.gate.native_proof(data, self.policy, set())

    def test_input_cleanup_fault_may_still_stop_worker_gracefully(self):
        data = self.native()
        data['result']['cleanup_fault_detected'] = True
        del data['result']['author_durable_revision']
        del data['result']['stop_exit']
        self.gate.native_proof(data, self.policy, set(), cleanup_negative=True)

    def test_forced_timing_missing_nonfinite_out_of_order_or_forged_rejected(self):
        life = self.forced_timing()
        negative_changes = [
            {'timing_clock': 'wall-clock'}, {'forced_started_at_ms': True},
            {'forced_started_at_ms': -1}, {'worker_kernel_exited_at_ms': 99},
            {'cleanup_completed_at_ms': 124}, {'forced_to_kernel_exit_ms': 24},
            {'kernel_exit_to_cleanup_complete_ms': -1},
            {'forced_to_kernel_exit_ms': float('inf')},
            {'cleanup_completed_at_ms': float('nan')}, {'cleanup_completed': False}]
        negative_changes += [{'stop_requested_at_ms': 101}, {'stop_published_at_ms': 126},
                             {'stop_requested_at_ms': None, 'stop_published_at_ms': 2}]
        for key in life:
            broken = dict(life); del broken[key]
            data = self.native()
            data['result']['cleanup_fault_detected'] = True
            data['result']['lifecycle'].update(graceful_stop=False, forced_stop=True)
            for field in life:
                data['result']['lifecycle'].pop(field, None)
            data['result']['lifecycle'].update(broken)
            with self.assertRaises(ValueError, msg='missing ' + key):
                self.gate.native_proof(data, self.policy, set(), cleanup_negative=True)
        for change in negative_changes:
            broken = dict(life); broken.update(change)
            data = self.native()
            data['result']['cleanup_fault_detected'] = True
            data['result']['lifecycle'].update(graceful_stop=False, forced_stop=True)
            data['result']['lifecycle'].update(broken)
            with self.assertRaises(ValueError, msg=str(change)):
                self.gate.native_proof(data, self.policy, set(), cleanup_negative=True)

    def test_owned_timeout_boolean_cleanup_cannot_replace_timing(self):
        data = {key: True for key in ('outer_job_assigned', 'client_kernel_exit_observed',
            'without_outer_cleanup_held', 'held_before_kill', 'native_released', 'outer_job_active_zero')}
        data.update(ledger_after_cleanup=0, published_injector_identities=[[123, 456]])
        with self.assertRaises(ValueError):
            self.gate.timeout_proof(data, set())

    def test_fault_registry_missing_or_new_unmapped_case_is_rejected(self):
        catalog = self.gate.engine.registry()
        self.gate.require_fault_catalog(catalog)
        pending = next(c for c in catalog if c.id == 'desktop-pending-move')
        with self.assertRaises(ValueError):
            self.gate.require_fault_catalog([c for c in catalog if c != pending])
        with self.assertRaises(ValueError):
            self.gate.require_fault_catalog(catalog + [replace(pending, id='new-unmapped-fault')])

    def test_exception_is_retained_and_no_next_cycle_runs(self):
        def fail(step):
            raise RuntimeError('cleanup-unproven')
        rows = self.gate.run_steps([{'id': 'one'}, {'id': 'two'}], fail, lambda _: None)
        self.assertEqual(len(rows), 1)
        self.assertIn('cleanup-unproven', rows[0]['error'])

    def test_final_evidence_mutation_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            path = folder/'report.json'; path.write_text('{}')
            log = folder/'run.log'; log.write_text('passed')
            row = {'artifact': {'path': str(path), 'files': self.gate.engine.inventory(folder)},
                   'log': str(log), 'log_sha256': self.gate.engine.digest(log)}
            self.gate.bind_final_evidence([row])
            path.write_text('{"changed": true}')
            with self.assertRaises(ValueError):
                self.gate.bind_final_evidence([row])

if __name__ == '__main__':
    unittest.main()

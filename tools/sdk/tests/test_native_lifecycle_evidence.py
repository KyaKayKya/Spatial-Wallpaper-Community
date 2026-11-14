import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))


class LifecycleEvidence(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(importlib.util.find_spec('native_lifecycle_evidence'), 'missing sidecar verifier')
        from native_lifecycle_evidence import attach
        self.attach = attach
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.life = {'status': 'observed-terminal', 'instance': 'b'*32, 'attempt': 'a'*32,
                     'worker_pid': 123, 'lifecycle': {'attempt': 'a'*32, 'worker_pid': 123,
                        'worker_started': True, 'worker_kernel_exited': True, 'cleanup_completed': True}}
        self.host = {'status': 'observed-kernel-exit', 'host_pid': 456,
                     'host_kernel_exited': True, 'retained_handle': True, 'exit_acknowledged': True}
        self.life.update(worker_creation_filetime=123456789, retained_worker_creation_filetime=123456789)
        self.life['lifecycle'].update(worker_creation_filetime=123456789, retained_worker_creation_filetime=123456789)
        self.host.update(host_creation_filetime=987654321, retained_host_creation_filetime=987654321)
        raw_attach = self.attach
        self.attach = lambda report, path, pid: raw_attach(report, path, pid, 987654321)

    def write(self):
        (self.path/'lifecycle.json').write_text(json.dumps(self.life))
        (self.path/'host-exit.json').write_text(json.dumps(self.host))

    def test_valid_proof_attached_without_inventing_timestamps(self):
        self.write(); report = {'result': {'cleanup_fault_detected': True}}
        self.attach(report, self.path, 456)
        self.assertEqual(report['result']['lifecycle'], self.life['lifecycle'])
        self.assertTrue(report['result']['cleanup_fault_detected'])
        self.assertEqual(len(report['lifecycle_sha256']), 64)

    def test_host_fallback_or_foreign_identity_cannot_pass(self):
        for field, value in [('status', 'failed'), ('host_pid', 789), ('host_kernel_exited', False),
                             ('retained_handle', False), ('exit_acknowledged', False)]:
            old = self.host[field]; self.host[field] = value; self.write(); report = {}
            with self.assertRaises(ValueError): self.attach(report, self.path, 456)
            self.assertEqual(report['host_exit_observation'][field], value)
            self.host[field] = old

    def test_missing_lifecycle_preserves_host_exit_diagnostics(self):
        self.host.update(status='failed', phase='wait-kernel', error='timeout')
        self.write(); (self.path/'lifecycle.json').unlink(); report = {}
        with self.assertRaises(ValueError): self.attach(report, self.path, 456)
        self.assertEqual(report['host_exit_observation']['phase'], 'wait-kernel')
        self.assertNotIn('result', report)

    def test_same_pid_reuse_and_unknown_creation_rejected(self):
        for obj, key in [(self.life, 'worker_creation_filetime'),
                         (self.life, 'retained_worker_creation_filetime'),
                         (self.life['lifecycle'], 'worker_creation_filetime'),
                         (self.life['lifecycle'], 'retained_worker_creation_filetime'),
                         (self.host, 'host_creation_filetime'),
                         (self.host, 'retained_host_creation_filetime')]:
            old = obj[key]
            for value in (None, True, 0, old + 1):
                obj[key] = value; self.write()
                with self.assertRaises(ValueError): self.attach({}, self.path, 456)
            obj[key] = old

    def test_mismatched_worker_or_unobserved_kernel_rejected(self):
        for field, value in [('attempt', 'c'*32), ('worker_pid', 124), ('worker_kernel_exited', False),
                             ('cleanup_completed', False), ('worker_started', False)]:
            old = self.life['lifecycle'][field]; self.life['lifecycle'][field] = value; self.write()
            with self.assertRaises(ValueError): self.attach({}, self.path, 456)
            self.life['lifecycle'][field] = old


if __name__ == '__main__': unittest.main()

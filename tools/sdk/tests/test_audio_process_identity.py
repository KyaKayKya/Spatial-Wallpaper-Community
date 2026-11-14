"""No audio, desktop, process suspension, or process termination is performed."""
import copy
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import audio_process_observation as observation


class AudioIdentity(unittest.TestCase):
    def setUp(self):
        self.host = SimpleNamespace(pid=10, _handle=101, poll=lambda: None)
        self.identity = dict(host_pid=10, host_creation_filetime=1000,
                             pid=20, creation_filetime=2000)
        self.child = Mock(pid=20, handle=202)
        self.child.exited.return_value = False

    def run_observe(self, identity=None, actual_host=(10, 1000), actual_child=(20, 2000)):
        with patch.object(observation, 'process_identity', side_effect=[actual_host, actual_child]), \
             patch.object(observation, 'ObservedProcess', return_value=self.child) as factory:
            result = observation.observe_audio_sampler(self.host, self.identity if identity is None else identity,
                                                       allow_suspend=True, allow_terminate=True)
            factory.assert_called_once_with(20, allow_terminate=True, allow_suspend=True)
            return result

    def test_retained_identity_matches_before_permission_is_used(self):
        result = self.run_observe()
        self.assertIs(result, self.child)
        self.child.suspend.assert_not_called()
        self.child.terminate.assert_not_called()
        self.child.close.assert_not_called()

    def test_pid_reuse_or_foreign_handle_never_suspended_or_terminated(self):
        for actual in ((20, 2001), (21, 2000)):
            self.child.reset_mock()
            with self.subTest(actual=actual), self.assertRaisesRegex(ValueError, 'audio-sampler-identity-mismatch'):
                self.run_observe(actual_child=actual)
            self.child.close.assert_called_once()
            self.child.suspend.assert_not_called()
            self.child.terminate.assert_not_called()

    def test_foreign_host_rejected_before_sampler_open(self):
        for actual in ((11, 1000), (10, 1001)):
            with patch.object(observation, 'process_identity', return_value=actual), \
                 patch.object(observation, 'ObservedProcess') as factory:
                with self.assertRaisesRegex(ValueError, 'audio-host-identity-mismatch'):
                    observation.observe_audio_sampler(self.host, self.identity, allow_suspend=True)
                factory.assert_not_called()

    def test_missing_unknown_or_non_integer_identity_rejected_without_open(self):
        for key in self.identity:
            for value in (None, True, 0, -1, str(self.identity[key])):
                bad = copy.deepcopy(self.identity); bad[key] = value
                with patch.object(observation, 'ObservedProcess') as factory:
                    with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                        observation.observe_audio_sampler(self.host, bad, allow_suspend=True)
                    factory.assert_not_called()

    def test_host_itself_and_preexisting_process_rejected_before_any_open(self):
        for changes in (dict(pid=10, creation_filetime=1000), dict(creation_filetime=999)):
            bad = dict(self.identity, **changes)
            with self.subTest(changes=changes), \
                 patch.object(observation, 'process_identity', side_effect=[(10, 1000), (bad['pid'], bad['creation_filetime'])]) as query, \
                 patch.object(observation, 'ObservedProcess', return_value=self.child) as factory:
                with self.assertRaisesRegex(ValueError, 'audio-process-identity-order'):
                    observation.observe_audio_sampler(self.host, bad, allow_suspend=True, allow_terminate=True)
                factory.assert_not_called()
                query.assert_not_called()

    def test_verified_object_is_retained_for_suspend_without_pid_reopen(self):
        with patch.object(observation, 'process_identity', side_effect=[(10, 1000), (20, 2000)]) as query, \
             patch.object(observation, 'ObservedProcess', return_value=self.child) as factory:
            retained = observation.observe_audio_sampler(self.host, self.identity, allow_suspend=True)
            retained.suspend()
            factory.assert_called_once_with(20, allow_terminate=False, allow_suspend=True)
            self.assertEqual([call.args[0] for call in query.call_args_list], [101, 202])
            self.assertIs(retained, self.child)
            self.child.suspend.assert_called_once_with()

    def test_kernel_query_failure_closes_only_opened_handle(self):
        with patch.object(observation, 'process_identity', side_effect=[(10, 1000), OSError('GetProcessTimes failed')]), \
             patch.object(observation, 'ObservedProcess', return_value=self.child):
            with self.assertRaisesRegex(OSError, 'GetProcessTimes'):
                observation.observe_audio_sampler(self.host, self.identity, allow_suspend=True)
        self.child.close.assert_called_once()
        self.child.suspend.assert_not_called()
        self.child.terminate.assert_not_called()


if __name__ == '__main__':
    unittest.main()

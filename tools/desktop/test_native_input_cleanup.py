import unittest
from unittest.mock import patch

from native_input_cleanup import owed_keys, wait_for_clear


class OwnedInputCleanupTests(unittest.TestCase):
    def test_recorded_native_preview_escape_is_released(self):
        self.assertEqual(owed_keys({'pid': 42, 'creation_filetime': 123, 'buttons': 4, 'keys': [27], 'injected': [1, 27]}, 42, 123), [1, 27])

    def test_last_moment_release_does_not_prove_stable_clear(self):
        clock = [0.0]
        def sleep(seconds):
            clock[0] += seconds
        with patch('native_input_cleanup.time.monotonic', side_effect=lambda: clock[0]), patch('native_input_cleanup.time.sleep', side_effect=sleep):
            self.assertFalse(wait_for_clear(lambda: clock[0] < .49))

    def test_clear_input_must_remain_clear_for_fifty_milliseconds(self):
        clock = [0.0]
        def sleep(seconds):
            clock[0] += seconds
        with patch('native_input_cleanup.time.monotonic', side_effect=lambda: clock[0]), patch('native_input_cleanup.time.sleep', side_effect=sleep):
            self.assertTrue(wait_for_clear(lambda: False))
        self.assertGreaterEqual(clock[0], .05)

    def test_only_recorded_buttons_and_keys_are_released(self):
        self.assertEqual(owed_keys({'pid': 42, 'creation_filetime': 123, 'buttons': 20, 'keys': [16, 32, 37]}, 42, 123), [1, 2, 16, 32, 37])

    def test_stale_process_identity_is_rejected(self):
        for pid, birth in [(43, 123), (42, 124)]:
            with self.assertRaises(ValueError):
                owed_keys({'pid': pid, 'creation_filetime': birth, 'buttons': 4, 'keys': []}, 42, 123)

    def test_completed_injection_still_has_parent_pairing_until_exit(self):
        self.assertEqual(owed_keys({'pid': 42, 'creation_filetime': 123, 'buttons': 0, 'keys': [], 'injected': [1, 32]}, 42, 123), [1, 32])

    def test_unrecorded_or_unknown_input_is_not_released(self):
        self.assertEqual(owed_keys({'pid': 42, 'creation_filetime': 123, 'buttons': 0, 'keys': []}, 42, 123), [])
        for buttons, keys in [(2, []), (0, [65]), (0, [32, 32])]:
            with self.assertRaises(ValueError):
                owed_keys({'pid': 42, 'creation_filetime': 123, 'buttons': buttons, 'keys': keys}, 42, 123)


if __name__ == '__main__':
    unittest.main()

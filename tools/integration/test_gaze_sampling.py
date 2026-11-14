"""Research data integrity regression checks; no camera or model loads."""
import json
import unittest

from sample_gaze_spike import safe_angles, target_summary


class SamplingChecks(unittest.TestCase):
    def test_nonfinite_angles_are_json_safe(self):
        self.assertIsNone(safe_angles([float('nan'), 1]))
        self.assertEqual(json.dumps({'angles':safe_angles([1,float('inf')])},allow_nan=False), '{"angles": null}')
        self.assertEqual(safe_angles([1,2]), [1,2])

    def test_completed_timer_is_not_valid_data(self):
        self.assertFalse(target_summary(0,0)['usable'])
        self.assertFalse(target_summary(13,0)['usable'])
        self.assertFalse(target_summary(13,4)['usable'])
        self.assertTrue(target_summary(13,5)['usable'])


if __name__=='__main__': unittest.main()

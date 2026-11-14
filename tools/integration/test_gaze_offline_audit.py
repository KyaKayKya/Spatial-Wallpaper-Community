"""Protect diagnostic target holdout and failed-capture boundaries."""
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np

from audit_gaze_offline import audit


class AuditChecks(unittest.TestCase):
    def records(self):
        records = [{'type': 'session', 'placement': 'test', 'pose': 'center'}]
        positions = [(x, y) for y in (-1, 0, 1) for x in (-1, 0, 1)]
        positions += [(-.5, -.5), (.5, -.5), (-.5, .5), (.5, .5)]
        for i, (x, y) in enumerate(positions):
            for _ in range(6):
                records.append({'type': 'sample', 'target_index': i, 'attempt': 1,
                                'split': 'fit' if i < 9 else 'held_out', 'valid': True,
                                'flags': [], 'pitch_yaw_deg_upstream': [x, y],
                                'target_desktop_px': [600+100*x, 400+80*y],
                                'window_rect': [100, 100, 1000, 650]})
        return records

    def run_audit(self, records):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'samples.jsonl'
            path.write_text('\n'.join(json.dumps(r) for r in records), encoding='utf-8')
            return audit(path)

    def test_held_out_labels_cannot_change_fit(self):
        records = self.records()
        before, coefficients, _, _ = self.run_audit(records)
        self.assertLess(before['held_frames']['euclidean_px_median_p95'][1], 1e-9)
        for r in records[1:]:
            if r['split'] == 'held_out': r['target_desktop_px'][0] += 200
        after, changed, _, _ = self.run_audit(records)
        np.testing.assert_array_equal(changed, coefficients)
        self.assertAlmostEqual(after['held_frames']['euclidean_px_median_p95'][0], 200)

    def test_failed_latest_capture_not_replaced_with_old_success(self):
        records = self.records()
        for r in list(records[1:]):
            if r['target_index'] == 0:
                records.append({**r, 'attempt': 2, 'valid': False, 'flags': ['no_face'],
                                'pitch_yaw_deg_upstream': None})
        with self.assertRaisesRegex(ValueError, 'got 8 / 4'): self.run_audit(records)

    def test_empty_latest_capture_not_replaced_with_old_success(self):
        records = self.records()
        records.append({'type': 'target_end', 'target_index': 0, 'attempt': 2,
                        'sample_count': 0, 'valid_count': 0, 'usable': False})
        with self.assertRaisesRegex(ValueError, 'got 8 / 4'): self.run_audit(records)


if __name__ == '__main__': unittest.main()

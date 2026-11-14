"""Historical audit remains a separate prerequisite to fresh E execution."""
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_e
from provenance import sources


class HistoricalGate(unittest.TestCase):
    def verdict(self, receipt, status='passed'):
        return verify_e.verdict([verify_e.Case('probe', '')],
                                [{'id': 'probe', 'status': status}], receipt=receipt)

    def test_current_incidents_remain_open_in_unified_report(self):
        result = self.verdict(sources())
        self.assertIn('historical_failure_closure', result)
        audit = result['historical_failure_closure']
        self.assertEqual(audit['status'], 'blocked')
        self.assertIn('input-first-hook-delay-a260', audit['open_incidents'])
        self.assertIn('host-exit-five-second-timeout', audit['open_incidents'])
        self.assertIn('historical-failure-closure', result['unimplemented_gates'])
        self.assertEqual(result['status'], 'failed')

    def test_accepted_history_preserves_current_failed_case(self):
        with patch('historical_failure_closure.evaluate_current', return_value={
                'status': 'passed-historical-closure', 'open_incidents': []}):
            result = self.verdict(sources(), 'failed')
        self.assertEqual(result['failed_cases'], ['probe'])
        self.assertEqual(result['status'], 'failed')

    def test_audit_failure_preserves_current_failed_case(self):
        for error in (ValueError('invalid'), OSError('missing'), ImportError('dependency')):
            with self.subTest(error=type(error).__name__), patch(
                    'historical_failure_closure.evaluate_current', side_effect=error):
                result = self.verdict(sources(), 'failed')
            self.assertEqual(result['historical_failure_closure']['status'], 'blocked')
            self.assertIn('historical-failure-closure', result['unimplemented_gates'])
            self.assertEqual(result['failed_cases'], ['probe'])

    def test_missing_or_changed_catalog_receipt_never_closes_gate(self):
        for receipt in (None, {}, {**sources(), 'tools/sdk/historical-failures.json': '0' * 64}):
            with self.subTest(receipt_missing=receipt is None):
                result = self.verdict(receipt)
                self.assertIn('historical-failure-closure', result['unimplemented_gates'])
                self.assertEqual(result['historical_failure_closure']['status'], 'blocked')

    def test_accepted_audit_cannot_substitute_for_fresh_E_cases(self):
        with patch('historical_failure_closure.evaluate_current', return_value={
                'status': 'passed-historical-closure', 'open_incidents': []}):
            result = self.verdict(sources())
        self.assertNotIn('historical-failure-closure', result['unimplemented_gates'])
        self.assertIn('interaction-1800', result['unimplemented_gates'])
        self.assertEqual(result['status'], 'failed')


if __name__ == '__main__':
    unittest.main()

import copy
import unittest

from analyze_native_bridge import assess


class BridgeTimingChecks(unittest.TestCase):
    def report(self):
        return {'status': 'captured-controlled-native-bridge-display-gpu-development-not-RC', 'inputs_unchanged': True,
                'cases': [{'name': eye + '-' + mode, 'start_qpc_ms': 1000, 'end_qpc_ms': 61000,
                           'timings': {'complete': True, 'completeness_failures': [],
                                       'displayed_duration_ms': {'p95': 16.7}, 'over_two_refresh_periods_fraction': .002}}
                          for eye in ['fixed', 'moving'] for mode in ['no-bridge', 'bridge', 'script-fault']]}

    def test_all_four_comparisons_use_original_limits(self):
        result = assess(self.report(), 60)
        self.assertEqual(result['status'], 'passed-development-comparison-not-RC')
        self.assertEqual(len(result['comparisons']), 4)
        self.assertAlmostEqual(result['comparisons'][0]['p95_limit_ms'], 18.37)
        self.assertEqual(result['comparisons'][0]['long_fraction_limit'], .003)

    def test_fault_long_frames_fail_the_entire_comparison(self):
        report = self.report()
        report['cases'][5]['timings']['over_two_refresh_periods_fraction'] = .003000001
        result = assess(report, 60)
        self.assertEqual(result['status'], 'failed-development-comparison')
        self.assertFalse(result['comparisons'][3]['long_fraction_pass'])

    def test_p95_regression_cannot_be_hidden_by_good_long_frame_fraction(self):
        report = self.report()
        report['cases'][1]['timings']['displayed_duration_ms']['p95'] = 18.371
        self.assertEqual(assess(report)['status'], 'failed-development-comparison')

    def test_incomplete_changed_reordered_or_short_captures_are_rejected(self):
        original = self.report()
        mutations = [lambda report: report.update(inputs_unchanged=False),
                     lambda report: report.update(status='failed'),
                     lambda report: report['cases'].reverse(),
                     lambda report: report['cases'].pop(),
                     lambda report: report['cases'][0]['timings'].update(complete=False),
                     lambda report: report['cases'][0].update(end_qpc_ms=60999)]
        for mutate in mutations:
            report = copy.deepcopy(original)
            mutate(report)
            with self.assertRaises(ValueError):
                assess(report, 60)


if __name__ == '__main__':
    unittest.main()

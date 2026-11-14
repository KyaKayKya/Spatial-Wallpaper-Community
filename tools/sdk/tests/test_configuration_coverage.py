import copy
import json
from pathlib import Path
import sys
import unittest

SDK = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SDK))
import configuration_coverage as api


class ConfigurationCoverage(unittest.TestCase):
    def setUp(self):
        self.policy = json.loads((SDK/'release-policy.json').read_text())
        self.identity = {'os': {'platform': 'Windows-test', 'machine': 'AMD64'},
                         'display_topology': [{'rectangle': [0, 0, 1920, 1080]}],
                         'dpi': [{'rectangle': [0, 0, 1920, 1080], 'effective_dpi': [96, 96]}],
                         'render_profile_id': 'test.profile', 'render_profile_version': '1.0.0',
                         'render_profile_format_version': 1}
        self.catalog = {'schema': 1, 'id': 'release-supported-configuration-catalog',
                        'status': 'candidate-requirements-not-verification',
                        'identity_dimensions': self.policy['coverage']['identity_dimensions'],
                        'render_profiles': [{'id': 'test.profile', 'version': '1.0.0', 'format_version': 1,
                                             'status': 'requested-unverified'}],
                        'configurations': [{'id': 'one', 'status': 'requested-unverified', 'identity': self.identity}]}

    def row(self, name, identity=None):
        observation = {key: {'status': 'measured', 'source': 'controlled observation', 'value': value}
                       for key, value in (identity or self.identity).items()}
        return {'id': name, 'status': 'passed', 'configuration_before': copy.deepcopy(observation),
                'configuration_after': copy.deepcopy(observation)}

    def evaluate(self, rows=None, catalog=None):
        return api.coverage(self.catalog if catalog is None else catalog, self.policy, ['a', 'b'],
                            [self.row('a'), self.row('b')] if rows is None else rows)

    def test_exact_multiconfiguration_cross_product(self):
        other = copy.deepcopy(self.identity); other['dpi'][0]['effective_dpi'] = [144, 144]
        self.catalog['configurations'].append({'id': 'two', 'status': 'requested-unverified', 'identity': other})
        result = self.evaluate([self.row('a'), self.row('b'), self.row('a', other), self.row('b', other)])
        self.assertEqual(result['status'], 'complete-current-run-configuration-coverage-only')
        self.assertFalse(result['support_claim'])
        self.assertEqual(result['covered'], {'one': ['a', 'b'], 'two': ['a', 'b']})
        self.assertEqual(self.evaluate()['status'], 'failed')

    def test_missing_empty_duplicate_and_supported_catalog_rejected(self):
        for mutate in [lambda c: c.update(configurations=[]),
                       lambda c: c['configurations'].append(copy.deepcopy(c['configurations'][0])),
                       lambda c: c.update(status='supported'),
                       lambda c: c['configurations'][0].update(status='supported')]:
            catalog = copy.deepcopy(self.catalog); mutate(catalog)
            self.assertEqual(self.evaluate(catalog=catalog)['status'], 'failed')
        self.assertEqual(api.coverage(None, self.policy, ['a'], [])['status'], 'failed')

    def test_unknown_extra_wildcard_and_bool_identity_rejected(self):
        for field, value in [('dpi', None), ('os', '*'), ('render_profile_version', '2.0.0'),
                             ('render_profile_format_version', True)]:
            catalog = copy.deepcopy(self.catalog); catalog['configurations'][0]['identity'][field] = value
            self.assertEqual(self.evaluate(catalog=catalog)['status'], 'failed')
        self.catalog['configurations'][0]['identity']['extra'] = 'ignored?'
        self.assertEqual(self.evaluate()['status'], 'failed')

    def test_missing_failed_duplicate_foreign_and_changed_case_rejected(self):
        for rows in [[], [self.row('a')], [self.row('a'), self.row('a'), self.row('b')],
                     [self.row('a'), dict(self.row('b'), status='failed')],
                     [self.row('a'), self.row('foreign')]]:
            self.assertEqual(self.evaluate(rows)['status'], 'failed')
        for key in self.identity:
            rows = [self.row('a'), self.row('b')]
            rows[1]['configuration_after'][key]['status'] = 'unmeasured'
            self.assertEqual(self.evaluate(rows)['status'], 'failed')
        rows = [self.row('a'), self.row('b')]
        rows[1]['configuration_after']['render_profile_version']['value'] = 'other'
        self.assertEqual(self.evaluate(rows)['status'], 'failed')

    def test_repository_candidate_is_not_support_or_complete_coverage(self):
        candidate = json.loads((SDK/'supported-configurations.json').read_text())
        result = api.coverage(candidate, self.policy, ['a'], [self.row('a')])
        self.assertEqual(result['status'], 'failed')
        self.assertFalse(result['support_claim'])

    def test_unified_verdict_only_closes_coverage_not_stage_e(self):
        import verify_e
        result = verify_e.verdict([verify_e.Case('a', 'a.py'), verify_e.Case('b', 'b.py')],
                                  [self.row('a'), self.row('b')], self.catalog, self.policy)
        self.assertEqual(result['status'], 'failed')
        self.assertNotIn('supported-configuration-catalog', result['unimplemented_gates'])
        self.assertIn('start-stop-100', result['unimplemented_gates'])
        self.assertFalse(result['configuration_coverage']['support_claim'])


if __name__ == '__main__': unittest.main()

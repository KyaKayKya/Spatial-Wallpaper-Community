"""Exact E coverage over fresh, already validated scheduler rows.

This is not an artifact importer or a platform certification authority. The
caller owns case/report validation. Candidate declarations never prove support.
"""
import json
import re

COMPLETE = 'complete-current-run-configuration-coverage-only'


def need(ok, reason):
    if not ok:
        raise ValueError(reason)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False)


def exact(value):
    if isinstance(value, dict):
        return bool(value) and all(type(k) is str and exact(v) for k, v in value.items())
    if isinstance(value, list):
        return bool(value) and all(exact(v) for v in value)
    if type(value) is str:
        # Strings are compared literally; Windows device identifiers may contain '?'.
        return value not in ('*', '?')
    return type(value) is int


def profile_key(identity):
    ident, version, schema = (identity.get(k) for k in
                             ('render_profile_id', 'render_profile_version', 'render_profile_format_version'))
    need(type(ident) is str and re.fullmatch('[a-z0-9][a-z0-9._-]*', ident), 'invalid-profile-id')
    need(type(version) is str and re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version), 'invalid-profile-version')
    need(type(schema) is int and schema > 0, 'invalid-profile-format-version')
    return ident, version, schema


def declarations(catalog, policy):
    need(isinstance(catalog, dict), 'missing-catalog')
    need(type(catalog.get('schema')) is int and catalog['schema'] == 1, 'catalog-schema')
    need(catalog.get('id') == policy['coverage']['source'], 'catalog-policy-id')
    need(catalog.get('status') == 'candidate-requirements-not-verification' and
         catalog.get('support_claim', False) is False, 'catalog-cannot-declare-support')
    dimensions = policy['coverage']['identity_dimensions']
    need(catalog.get('identity_dimensions') == dimensions, 'catalog-policy-dimensions')
    profiles = set()
    need(type(catalog.get('render_profiles')) is list, 'missing-profile-catalog')
    for entry in catalog['render_profiles']:
        need(entry.get('status') == 'requested-unverified', 'profile-cannot-declare-support')
        key = profile_key({'render_profile_id': entry.get('id'), 'render_profile_version': entry.get('version'),
                           'render_profile_format_version': entry.get('format_version')})
        need(key not in profiles, 'duplicate-profile'); profiles.add(key)
    need(type(catalog.get('configurations')) is list and catalog['configurations'], 'empty-configuration-catalog')
    targets, identities = {}, set()
    for entry in catalog['configurations']:
        ident = entry.get('id'); identity = entry.get('identity')
        need(type(ident) is str and re.fullmatch('[a-z0-9][a-z0-9._-]*', ident), 'invalid-configuration-id')
        need(ident not in targets, 'duplicate-configuration-id')
        need(entry.get('status') == 'requested-unverified', 'configuration-cannot-declare-support')
        need(isinstance(identity, dict) and set(identity) == set(dimensions), 'configuration-dimensions')
        need(exact(identity), 'unresolved-or-inexact-configuration:' + ident)
        need(isinstance(identity['os'], dict) and isinstance(identity['display_topology'], list) and
             isinstance(identity['dpi'], list), 'configuration-value-shape')
        need(profile_key(identity) in profiles, 'unknown-profile-reference:' + ident)
        key = canonical(identity); need(key not in identities, 'duplicate-configuration-identity')
        targets[ident] = key; identities.add(key)
    return dimensions, targets


def observed(observation, dimensions):
    need(isinstance(observation, dict) and set(observation) == set(dimensions), 'observation-dimensions')
    identity = {}
    for key in dimensions:
        entry = observation[key]
        need(isinstance(entry, dict) and entry.get('status') == 'measured' and
             type(entry.get('source')) is str and bool(entry['source']) and exact(entry.get('value')),
             'unmeasured-configuration:' + key)
        identity[key] = entry['value']
    profile_key(identity)
    return canonical(identity)


def coverage(catalog, policy, case_ids, rows):
    result = {'status': 'failed', 'support_claim': False, 'covered': {}, 'missing': [], 'errors': []}
    try:
        dimensions, targets = declarations(catalog, policy)
        required = set(case_ids)
        need(required and len(required) == len(case_ids), 'invalid-required-case-set')
        seen = {name: set() for name in targets}
        reverse = {identity: name for name, identity in targets.items()}
        for index, row in enumerate(rows):
            try:
                need(row.get('id') in required, 'unknown-case')
                need(row.get('status') == 'passed', 'case-not-passed')
                before = observed(row.get('configuration_before'), dimensions)
                after = observed(row.get('configuration_after'), dimensions)
                need(before == after, 'configuration-drift')
                need(before in reverse, 'undeclared-configuration')
                ident = reverse[before]
                need(row['id'] not in seen[ident], 'duplicate-configuration-case')
                seen[ident].add(row['id'])
            except (ValueError, TypeError, KeyError) as error:
                result['errors'].append({'row': index, 'reason': str(error)})
        result['covered'] = {name: sorted(value) for name, value in seen.items()}
        result['missing'] = [{'configuration': name, 'case': case} for name, value in seen.items()
                             for case in sorted(required - value)]
        if not result['errors'] and not result['missing']:
            result['status'] = COMPLETE
    except (ValueError, TypeError, KeyError, AttributeError) as error:
        result['errors'].append({'reason': str(error)})
    return result

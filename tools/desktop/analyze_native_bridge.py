"""Apply the existing native bridge development thresholds to all six captured cases."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def assess(report, minimum_seconds=10):
    expected = [eye + '-' + mode for eye in ['fixed', 'moving'] for mode in ['no-bridge', 'bridge', 'script-fault']]
    if report.get('status') != 'captured-controlled-native-bridge-display-gpu-development-not-RC' or not report.get('inputs_unchanged'):
        raise ValueError('capture or frozen-input verification incomplete')
    cases = report['cases']
    if [case['name'] for case in cases] != expected:
        raise ValueError('six complete cases in declared order required')
    for case in cases:
        timings = case['timings']
        if not timings.get('complete') or timings.get('completeness_failures'):
            raise ValueError('incomplete displayed/GPU timing: ' + case['name'])
        if case['end_qpc_ms'] - case['start_qpc_ms'] < minimum_seconds * 1000:
            raise ValueError('measurement shorter than declared duration: ' + case['name'])
        for value in [timings['displayed_duration_ms']['p95'], timings['over_two_refresh_periods_fraction']]:
            if not math.isfinite(value) or value < 0:
                raise ValueError('invalid performance metric: ' + case['name'])
    comparisons = []
    for offset in [0, 3]:
        baseline = cases[offset]['timings']
        p95_limit = max(baseline['displayed_duration_ms']['p95'] * 1.10, baseline['displayed_duration_ms']['p95'] + 1)
        long_limit = baseline['over_two_refresh_periods_fraction'] + .001
        for case in cases[offset + 1:offset + 3]:
            timings = case['timings']
            comparisons.append({'case': case['name'], 'p95_limit_ms': p95_limit,
                                'p95_ms': timings['displayed_duration_ms']['p95'],
                                'p95_pass': timings['displayed_duration_ms']['p95'] <= p95_limit,
                                'long_fraction_limit': long_limit,
                                'long_fraction': timings['over_two_refresh_periods_fraction'],
                                'long_fraction_pass': timings['over_two_refresh_periods_fraction'] <= long_limit})
    passed = all(case['p95_pass'] and case['long_fraction_pass'] for case in comparisons)
    return {'status': 'passed-development-comparison-not-RC' if passed else 'failed-development-comparison',
            'minimum_seconds_per_case': minimum_seconds, 'comparisons': comparisons}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--minimum-seconds', type=int, choices=[10, 60], default=10)
    args = parser.parse_args()
    path = args.folder / 'report.json'
    result = assess(json.loads(path.read_text(encoding='utf-8-sig')), args.minimum_seconds)
    result['capture_report_sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    result['analysis_source_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (args.folder / 'analysis.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
    return 0 if result['status'] == 'passed-development-comparison-not-RC' else 1


if __name__ == '__main__':
    raise SystemExit(main())

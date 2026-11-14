"""Reanalyze a completed, immutable ETW capture without another UAC or render run."""
import argparse
import hashlib
import json
from pathlib import Path
from check_native_baseline import ORIGINALS, read, summarize, require_complete_timing, observation


def analyze(folder):
    report = read(folder / 'report.json')
    csv = folder / 'presents.csv'
    result = {'scope': '60-second original native development baseline; not RC',
              'capture_report_sha256': hashlib.sha256((folder / 'report.json').read_bytes()).hexdigest(),
              'csv_sha256': hashlib.sha256(csv.read_bytes()).hexdigest(),
              'analysis_sources': {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                                   for name in ('analyze_native_baseline.py', 'check_native_baseline.py')},
              'assembly_sha256': report['assembly_sha256'], 'presentmon_sha256': report['presentmon_sha256'],
              'capture_status': report['status'], 'capture_error': report.get('error'),
              'target': report['target'], 'primary_display_refresh_hz': report['primary_display_refresh_hz'],
              'not_measured': report['not_measured'], 'cases': []}
    # A completed acquisition may have failed only in the old CSV-column parser.
    # Preserve that failure; never rehabilitate a capture/cleanup failure.
    parser_failure = report['status'] == 'failed' and report.get('error') == "AssertionError('ETW QPC time boundary missing')"
    result['reanalyzed_known_column_parser_failure'] = parser_failure
    complete = report['status'] == 'captured-native-actual-display-gpu-development-baseline-not-RC' or parser_failure
    complete = complete and report.get('leftover_owned_players') == [] and not any(key in report for key in
        ('cleanup_error', 'quit_error', 'client_close_error', 'monitor_cleanup_error', 'fallback_closed_owned_players'))
    complete = complete and [case['artwork'] for case in report['cases']] == ORIGINALS
    for case in report['cases']:
        for phase in ('before', 'after'):
            snapshot = read(folder / case['artwork'] / (phase + '.json'))
            observation(snapshot, case['artwork'])
            complete = complete and snapshot['pid'] == report['pid'] and snapshot['render_errors'] == 0 and snapshot['camera_started']
        start, end = case['start_qpc_ms'], case['end_qpc_ms']
        timings = summarize(csv, report['pid'], start, end, report['primary_display_refresh_hz'])
        valid = require_complete_timing(timings, end - start, report['primary_display_refresh_hz'])
        complete = complete and valid and 60000 <= end - start <= 62000
        samples = read(folder / case['artwork'] / 'process.json')
        result['cases'].append({'artwork': case['artwork'], 'start_qpc_ms': start, 'end_qpc_ms': end,
                                'timings': timings, 'process_samples': len(samples),
                                'cpu_percent_one_core_units_mean': sum(s['cpu_percent_one_core_units'] for s in samples) / len(samples),
                                'rss_bytes_peak': max(s['rss_bytes'] for s in samples)})
    result['status'] = 'verified-original-native-development-baseline-not-RC' if complete else 'incomplete-native-baseline'
    (folder / 'analysis.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    result = analyze(parser.parse_args().folder)
    print(result['status'])
    for case in result['cases']:
        print(case['artwork'], case['timings']['completeness_failures'])
    raise SystemExit(0 if result['status'].startswith('verified-') else 1)

"""Separate observed GPU work, GPU wait and presentation policy; no gate changes."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path


def metric(values):
    if not values:
        return None
    values.sort()
    return {'samples': len(values), **{label: values[min(len(values) - 1, math.ceil(len(values) * fraction) - 1)]
                                     for label, fraction in [('p50', .5), ('p95', .95), ('p99', .99)]}}


def inspect_capture(path, pid, start, end):
    columns = {'gpu_time_ms': 'MsGPUTime', 'gpu_busy_ms': 'MsGPUBusy', 'gpu_wait_ms': 'MsGPUWait'}
    values = {label: [] for label in columns}
    count, sync, modes = 0, set(), set()
    with path.open(encoding='utf-8-sig', newline='') as capture:
        for row in csv.DictReader(capture):
            if int(row['ProcessID']) != pid or not start <= float(row['CPUStartQPCTimeInMs']) < end:
                continue
            count += 1
            sync.add(int(row['SyncInterval']))
            modes.add(row['PresentMode'])
            for label, column in columns.items():
                try:
                    value = float(row[column])
                except (ValueError, KeyError):
                    continue
                if math.isfinite(value) and value >= 0:
                    values[label].append(value)
    return {'rows': count, 'sync_intervals': sorted(sync), 'present_modes': sorted(modes),
            **{label: metric(samples) for label, samples in values.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    args = parser.parse_args()
    report_path = args.folder / 'report.json'
    report = json.loads(report_path.read_text(encoding='utf-8-sig'))
    root = Path(__file__).resolve().parents[2]
    result = {'scope': __doc__, 'capture_report_sha256': hashlib.sha256(report_path.read_bytes()).hexdigest(),
              'analysis_source_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'cases': []}
    for case in report['cases']:
        path = root / report['run'] / case['name'] / 'presents.csv'
        observed = inspect_capture(path, case['pid'], case['start_qpc_ms'], case['end_qpc_ms'])
        result['cases'].append({'name': case['name'], 'csv_sha256': hashlib.sha256(path.read_bytes()).hexdigest(), **observed})
    (args.folder / 'presentation-diagnostics.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()

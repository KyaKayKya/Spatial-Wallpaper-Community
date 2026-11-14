"""Numeric-only 2D diagnostic. No camera, model load, or preset writes."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def load(path, gaze_only):
    records = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]
    samples = [r for r in records if r['type'] == 'sample']
    attempts = [r for r in records if r['type'] in ('sample', 'target_end')]
    latest = {i: max(r.get('attempt', 0) for r in attempts if r['target_index'] == i)
              for i in {r['target_index'] for r in attempts}}
    rows = [r for r in samples if r.get('attempt', 0) == latest[r['target_index']]]
    def usable(r):
        angles = r.get('pitch_yaw_deg_upstream')
        if angles is None or not np.isfinite(angles).all(): return False
        return r['valid'] or (gaze_only and not set(r['flags']).difference({'invalid_pose', 'low_geometry_quality'}))
    return records[0], rows, [r for r in rows if usable(r)]


def errors(predicted, truth):
    delta = np.asarray(predicted)-np.asarray(truth)
    return {'euclidean_px_median_p95': np.percentile(np.linalg.norm(delta, axis=1), [50, 95]).tolist(),
            'absolute_xy_px_median': np.median(np.abs(delta), axis=0).tolist(), 'n': len(delta)}


def audit(path, gaze_only=False):
    meta, raw, rows = load(path, gaze_only)
    groups = []
    for i in sorted({r['target_index'] for r in rows}):
        group = [r for r in rows if r['target_index'] == i]
        if len(group) < 5: continue
        angles = np.array([r['pitch_yaw_deg_upstream'] for r in group])
        targets = np.array([r['target_desktop_px'] for r in group])
        if np.ptp(targets, axis=0).max() > .01: raise ValueError('Target moved within capture')
        groups.append({'index': i, 'split': group[0]['split'], 'count': len(group),
                       'angle': np.median(angles, axis=0), 'target': targets[0],
                       'std_deg': angles.std(axis=0)})
    train = [g for g in groups if g['split'] == 'fit']
    held = [g for g in groups if g['split'] == 'held_out']
    if len(train) != 9 or len(held) != 4:
        raise ValueError(f'Need 9 fit / 4 held-out targets with >=5 samples; got {len(train)} / {len(held)}')
    design = np.array([[*g['angle'], 1] for g in train])
    if np.linalg.matrix_rank(design) != 3: raise ValueError('Degenerate angle design')
    coefficients = np.linalg.lstsq(design, np.array([g['target'] for g in train]), rcond=None)[0]
    def predict(a): return np.column_stack((np.asarray(a), np.ones(len(a)))) @ coefficients
    test = [r for r in rows if r['split'] == 'held_out']
    report = {'session': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
              'placement': meta['placement'], 'pose': meta['pose'], 'gaze_only': gaze_only,
              'last_attempt_samples': len(raw), 'usable_samples': len(rows),
              'window_rects': sorted({tuple(r['window_rect']) for r in rows}),
              'target_bounds_px': [np.min([g['target'] for g in groups], axis=0).tolist(),
                                   np.max([g['target'] for g in groups], axis=0).tolist()],
              'fit_target_medians': errors(predict([g['angle'] for g in train]), [g['target'] for g in train]),
              'held_target_medians': errors(predict([g['angle'] for g in held]), [g['target'] for g in held]),
              'held_frames': errors(predict([r['pitch_yaw_deg_upstream'] for r in test]), [r['target_desktop_px'] for r in test]),
              'median_within_target_std_deg_xy': np.median([g['std_deg'] for g in groups], axis=0).tolist(),
              'affine_coefficients': coefficients.tolist(),
              'limitations': ['conditional on instructed fixation; not independent gaze truth',
                              '4 held-out targets in same session, correlated frames',
                              'no physical angular or 3D position accuracy claim',
                              'no images: crop and raw inference cannot be rechecked']}
    return report, coefficients, meta, rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sessions', nargs='+', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    reports, cache = [], {}
    for path in args.sessions:
        for mode in (False, True):
            try:
                result = audit(path, mode); cache[path, mode] = result; reports.append(result[0])
            except ValueError as error:
                reports.append({'session': path.name, 'gaze_only': mode, 'not_evaluated': str(error)})
    transfers = []
    for source in args.sessions:
        # Use gaze-only to keep pose failures from hiding the 2D gaze diagnostic.
        if (source, True) not in cache: continue
        a, coefficients, meta, rows = cache[source, True]
        for target in args.sessions:
            if (target, True) not in cache: continue
            b, _, other, other_rows = cache[target, True]
            if source == target or meta['placement'] != other['placement'] or a['window_rects'] != b['window_rects']: continue
            prediction = np.array([[*r['pitch_yaw_deg_upstream'], 1] for r in other_rows]) @ coefficients
            transfers.append({'source': source.name, 'target': target.name,
                              **errors(prediction, [r['target_desktop_px'] for r in other_rows]),
                              'limitation': 'head position changes true ray geometry; cannot attribute solely to model bias'})
    output = {'sessions': reports, 'same_placement_transfers': transfers,
              'physical_accuracy_verified': False}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, indent=2, allow_nan=False), encoding='utf-8')
    for r in reports:
        if 'not_evaluated' in r:
            print(r); continue
        print(r['placement'], r['pose'], 'gaze_only', r['gaze_only'],
              'target', r['held_target_medians']['euclidean_px_median_p95'],
              'frame', r['held_frames']['euclidean_px_median_p95'])
    print('Transfers:', json.dumps(transfers)); print('Report:', args.output)


if __name__ == '__main__': main()

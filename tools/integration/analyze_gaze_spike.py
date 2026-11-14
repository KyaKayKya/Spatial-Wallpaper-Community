"""Summarize numeric sessions. Affine angle-to-window fit is a diagnostic, not calibration."""
import argparse
from collections import Counter
import json
from pathlib import Path

import numpy as np


def session(path, gaze_only=False):
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]
    metadata = next(row for row in rows if row['type'] == 'session')
    samples = [row for row in rows if row['type'] == 'sample']
    groups = []
    for index in sorted({row['target_index'] for row in samples}):
        raw = [row for row in samples if row['target_index'] == index]
        attempt = max(row.get('attempt',0) for row in raw)
        raw = [row for row in raw if row.get('attempt',0)==attempt]
        valid = [row for row in raw if row['valid'] or (gaze_only and
                 row.get('pitch_yaw_deg_upstream') is not None and
                 np.isfinite(row['pitch_yaw_deg_upstream']).all() and
                 not set(row['flags']).difference({'invalid_pose','low_geometry_quality'}))]
        group = {'index':index,'count':len(raw),'valid_count':len(valid),
                 'target_uv':raw[0]['target_uv'],'split':raw[0]['split']}
        if valid:
            angles = np.array([row['pitch_yaw_deg_upstream'] for row in valid])
            group.update(angles_deg_median=np.median(angles,axis=0).tolist(),
                         angles_deg_std=np.std(angles,axis=0).tolist())
        groups.append(group)
    return metadata,samples,groups


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sessions',nargs='+',type=Path)
    parser.add_argument('--gaze-only',action='store_true',help='Diagnostic only: include gaze even when eye position failed')
    parser.add_argument('--brief',action='store_true',help='Omit per-target values')
    args = parser.parse_args()
    first = None
    for path in args.sessions:
        metadata,samples,groups = session(path,args.gaze_only)
        report = {'file':str(path),'placement':metadata['placement'],'pose':metadata['pose'],
                  'sample_count':len(samples),'valid_count':sum(row['valid'] for row in samples),
                  'flag_counts':dict(Counter(flag for row in samples for flag in row['flags'])),
                  'targets':groups,'accuracy_claim':'none: diagnostic only, no independent geometry truth'}
        report['diagnostic_mask'] = 'gaze_only' if args.gaze_only else 'joint_gaze_and_eye_position'
        if samples:
            report['inference_ms_median_p95'] = np.percentile([row['inference_ms'] for row in samples],[50,95]).tolist()
        valid_groups = [g for g in groups if g['valid_count'] >= 5]
        train = [g for g in valid_groups if g['split']=='fit']
        if first is None and len(train)==9:
            x = np.array([[*g['angles_deg_median'],1] for g in train])
            if np.linalg.matrix_rank(x)==3:
                coefficients = np.linalg.lstsq(x,np.array([g['target_uv'] for g in train]),rcond=None)[0]
                first = (coefficients,path,metadata,{tuple(s['window_rect']) for s in samples})
                report['affine_diagnostic_coefficients'] = coefficients.tolist()
        if first is not None:
            coefficients,training_path,training_meta,rectangles = first
            # Do not mix camera placements or moved windows under a single 2D diagnostic.
            candidates = [g for g in valid_groups if path!=training_path or g['split']=='held_out']
            same_placement = metadata['placement']==training_meta['placement'] and rectangles=={tuple(s['window_rect']) for s in samples} and len(rectangles)==1
            if same_placement and candidates:
                predictions = np.array([[*g['angles_deg_median'],1] for g in candidates]) @ coefficients
                errors = predictions-np.array([g['target_uv'] for g in candidates])
                report['held_out_affine_window_uv'] = {
                    'training_file':str(training_path),'target_count':len(candidates),
                    'absolute_xy_median':np.median(np.abs(errors),axis=0).tolist(),
                    'absolute_xy_p95':np.percentile(np.abs(errors),95,axis=0).tolist(),
                    'limitation':'target medians; angle-only affine fit; not physical gaze or wallpaper accuracy'}
        if args.brief: report.pop('targets')
        print(json.dumps(report,indent=2,ensure_ascii=False))


if __name__=='__main__': main()

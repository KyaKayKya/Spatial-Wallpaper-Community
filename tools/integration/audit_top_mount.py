"""Offline top-edge/coplanar/zero-roll hypothesis, never a preset exporter."""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np

from audit_gaze_offline import load
from solve_gaze_candidate import gaze_directions, evaluate


def pose(p):
    # Camera local x stays horizontal in screen coordinates: no optical roll.
    q = cv2.Rodrigues(np.array([0., p[0], 0.]))[0] @ cv2.Rodrigues(np.array([p[1], 0., 0.]))[0] @ np.diag([-1., 1., -1.])
    c = np.array([p[2], p[3], 0.])
    r = q.T
    return r, -r@c, np.exp(p[4]) if len(p) == 5 else 1.


def rays(p, targets, eyes):
    r, t, scale = pose(p)
    v = targets@r.T+t-scale*eyes
    return v/np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-12)


def fit(targets, eyes, gaze, weights, joint_scale, screen_height):
    # Wide numerical guardrails, not a claim that all devices satisfy these ranges.
    low = np.array([-1.35, -1.35, -1., screen_height/2]+([np.log(.5)] if joint_scale else []))
    high = np.array([1.35, 1.35, 1., 1.]+([np.log(2.)] if joint_scale else []))
    solutions = []
    def residual(p): return rays(p, targets, eyes)-gaze
    def cost(r):
        n = np.linalg.norm(r, axis=1)
        return np.sum(weights*np.where(n <= .05, .5*n*n, .05*(n-.025)))
    for yaw in (-.5, 0., .5):
        for pitch in (-.3, 0., .3):
            p = np.array([yaw, pitch, 0., screen_height/2+.02]+([0.] if joint_scale else []))
            damping = .001
            for _ in range(140):
                res = residual(p)
                robust = np.sqrt(weights*np.minimum(1, .05/np.maximum(np.linalg.norm(res, axis=1), 1e-12)))
                j = []
                for i in range(len(p)):
                    step = np.zeros(len(p)); step[i] = 1e-5
                    j.append(((residual(p+step)-residual(p-step))*robust[:, None]/2e-5).ravel())
                j = np.array(j).T
                gradient = j.T@(res*robust[:, None]).ravel()
                free = ~(((p <= low+1e-9) & (gradient > 0)) | ((p >= high-1e-9) & (gradient < 0)))
                delta = np.zeros(len(p))
                if not free.any(): break
                reduced = j[:, free]
                delta[free] = np.linalg.solve(reduced.T@reduced+damping*np.eye(int(free.sum())), -gradient[free])
                trial = np.clip(p+delta, low, high)
                if cost(residual(trial)) < cost(res):
                    change = np.linalg.norm(trial-p); p = trial; damping = max(damping/3, 1e-9)
                    if change < 1e-8: break
                else: damping = min(damping*10, 1e10)
            r, t, scale = pose(p)
            mapped = (scale*eyes-t)@r
            if np.all(mapped[:, 2] < -.05): solutions.append((float(cost(residual(p))), p))
    if not solutions: raise ValueError('No front-screen constrained solution')
    solutions.sort(key=lambda x: x[0])
    return solutions[0][1], solutions


def real_session(path):
    meta, _, rows = load(path, False)
    if meta['placement'] != 'top': raise ValueError('Top placement required')
    xy = np.array([r['target_desktop_px'] for r in rows])
    targets = np.column_stack(((xy[:, 0]-960)*.53/1920, (540-xy[:, 1])*.53/1920, np.zeros(len(rows))))
    eyes = np.array([r['eye_position'] for r in rows])
    gaze = gaze_directions([r['pitch_yaw_deg_upstream'] for r in rows])
    train = np.array([r['split'] == 'fit' for r in rows])
    weights = np.array([1/sum(x['target_index'] == r['target_index'] for x in rows) for r in rows])
    reports = []
    for joint in (False, True):
        p, solutions = fit(targets[train], eyes[train], gaze[train], weights[train], joint, .53*1080/1920)
        r, t, s = pose(p); raw_pose = np.r_[cv2.Rodrigues(r)[0].ravel(), t]
        reports.append({'joint_scale': joint, 'parameters': p.tolist(), 'scale': float(s),
                        'reference_eye': ((s*eyes[train].mean(axis=0)-t)@r).tolist(),
                        'camera_in_screen': [float(p[2]), float(p[3]), 0.],
                        'camera_height_at_lower_bound': bool(abs(p[3]-.53*1080/1920/2) < 1e-6),
                        'train': evaluate(raw_pose, targets[train], eyes[train], gaze[train], s, .53, 1920),
                        'held_out': evaluate(raw_pose, targets[~train], eyes[~train], gaze[~train], s, .53, 1920),
                        'multistart_costs': [c for c, _ in solutions],
                        'accepted_calibration': False})
    return reports


def synthetic():
    records = []
    truth = np.array([.2, .15, .09, .17]); r, t, _ = pose(truth)
    targets = np.repeat([[x, y, 0] for y in [-.1, 0, .1] for x in [-.18, 0, .18]], 8, axis=0)
    for scenario in ('clean', 'noise_1deg', 'bias_5deg', 'eye_scale_10pct', 'mount_depth_2cm', 'roll_3deg'):
        actual_r, actual_t = r.copy(), t.copy()
        if scenario == 'mount_depth_2cm': actual_t = -actual_r@np.array([.09, .17, -.02])
        if scenario == 'roll_3deg':
            actual_r = cv2.Rodrigues(np.array([0., 0., np.deg2rad(3)]))[0]@r
            actual_t = -actual_r@np.array([.09, .17, 0.])
        rng = np.random.default_rng(19)
        e = np.array([0., .02, -.6])+rng.uniform(-.01, .01, targets.shape)
        q = e@actual_r.T+actual_t
        v = targets@actual_r.T+actual_t-q; gaze = v/np.linalg.norm(v, axis=1, keepdims=True)
        if scenario == 'noise_1deg': gaze = np.array([cv2.Rodrigues(a)[0]@g for a, g in zip(rng.normal(0, np.deg2rad(1), gaze.shape), gaze)])
        if scenario == 'bias_5deg': gaze = gaze@cv2.Rodrigues(np.array([0., np.deg2rad(5), 0.]))[0].T
        factor = 1.1 if scenario == 'eye_scale_10pct' else 1.
        probes = np.array([[x, y, z] for x in [-.12, 0, .12] for y in [-.08, .02, .12] for z in [-.45, -.6, -.8]])
        for joint in (False, True):
            p, _ = fit(targets, q*factor, gaze, np.ones(len(q))/8, joint, .53*1080/1920)
            rr, tt, ss = pose(p)
            mapped = (ss*(probes@actual_r.T+actual_t)*factor-tt)@rr
            error = np.linalg.norm(mapped-probes, axis=1)*1000
            if scenario == 'clean' or (scenario == 'eye_scale_10pct' and joint): assert error.max() < .01
            records.append({'scenario': scenario, 'joint_scale': joint, 'scale': float(ss),
                            'probe_eye_error_mm_median_max': np.percentile(error, [50, 100]).tolist()})
    return records


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('session', type=Path); parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = {'synthetic': synthetic(), 'real_top': real_session(args.session),
              'assumptions': ['camera optical center coplanar with screen, above top edge',
                              'optical roll zero, yaw/pitch free', 'screen nominal .53m / 1920x1080',
                              'synthetic is one seed / one mount; not reliability proof'],
              'scope': 'offline hypothesis, no preset export, no acceptance'}
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False), encoding='utf-8')
    print(json.dumps(report, indent=2))

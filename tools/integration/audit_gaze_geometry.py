"""Offline stress tests of the existing solver, with known synthetic truth.

Perturbations are sensitivity probes, not measured webcam error distributions.
No camera, model inference, UI, or production preset writes.
"""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np

from solve_gaze_candidate import camera_to_screen, fit_multistart, predicted
from gaze_geometry_checks import fit as fit_local_scale, jacobian as scale_jacobian


def unit(v): return v / np.linalg.norm(v, axis=1, keepdims=True)


def degrees(a, b):
    return np.rad2deg(np.arccos(np.clip(np.sum(a*b, axis=1), -1, 1)))


def perturb_eyes(q, scenario):
    if scenario == 'eye_scale_10pct': return q*1.1
    if scenario == 'eye_depth_10pct': return q*np.array([1., 1., 1.1])
    if scenario == 'eye_origin_1cm': return q+np.array([.01, 0, 0])
    return q.copy()


def perturb_gaze(g, scenario, rng):
    if scenario.startswith('noise_'):
        sigma = float(scenario.split('_')[1])
        rotations = rng.normal(0, np.deg2rad(sigma), (len(g), 3))
        return np.array([cv2.Rodrigues(r)[0] @ ray for r, ray in zip(rotations, g)])
    if scenario == 'bias_5deg': return g @ cv2.Rodrigues(np.array([0., np.deg2rad(5), 0.]))[0].T
    if scenario == 'horizontal_gain_10pct': return unit(g*np.array([1.1, 1, 1]))
    return g.copy()


def scene(placement):
    angle, location = {'top': (0., [0, .18, 0]),
                       'left': (-.45, [-.35, .08, 0]),
                       'right': (.45, [.35, .08, 0])}[placement]
    rotation = cv2.Rodrigues(np.array([0., np.pi+angle, 0.]))[0]
    translation = -rotation @ np.array(location)
    return rotation, translation, np.r_[cv2.Rodrigues(rotation)[0].ravel(), translation]


def one(placement, motion, scenario, seed):
    rng = np.random.default_rng(seed)
    rotation, translation, truth = scene(placement)
    targets = np.repeat([[x, y, 0] for y in [-.10, 0, .10] for x in [-.18, 0, .18]], 8, axis=0)
    amplitude = {'fixed': 0., 'small': .01, 'diverse': .10}[motion]
    screen_eyes = np.array([0, .02, -.60])+rng.uniform(-amplitude, amplitude, targets.shape)
    true_q = screen_eyes @ rotation.T+translation
    observed_q = perturb_eyes(true_q, scenario)
    gaze = perturb_gaze(predicted(truth, targets, true_q, 1.), scenario, rng)
    fit, solutions = fit_multistart(targets, observed_q, gaze, np.ones(len(targets))/8, 1.)
    result_rotation = cv2.Rodrigues(fit[:3])[0]
    probes = np.array([[x, y, z] for x in [-.12, 0, .12] for y in [-.08, .02, .12] for z in [-.45, -.60, -.80]])
    probes_q = perturb_eyes(probes @ rotation.T+translation, scenario)
    mapped = camera_to_screen(fit, probes_q, 1.)
    errors = np.linalg.norm(mapped-probes, axis=1)*1000
    # New target locations and eye positions, never used in fitting.
    held_targets = np.tile([[x, y, 0] for x in [-.09, .09] for y in [-.05, .05]], (8, 1))
    held_eyes = np.array([0, .02, -.60])+rng.uniform(-.10, .10, held_targets.shape)
    held_q = held_eyes @ rotation.T+translation
    held_gaze = perturb_gaze(predicted(truth, held_targets, held_q, 1.), scenario, rng)
    reference = camera_to_screen(fit, perturb_eyes(np.array([0, .02, -.60]) @ rotation.T+translation, scenario), 1.)
    different = [cost for cost, p in solutions if np.linalg.norm(cv2.Rodrigues(cv2.Rodrigues(p[:3])[0].T@result_rotation)[0]) > np.deg2rad(5)]
    return {'placement': placement, 'motion': motion, 'scenario': scenario, 'seed': seed,
            'rotation_error_deg': float(np.linalg.norm(cv2.Rodrigues(result_rotation.T@rotation)[0])*180/np.pi),
            'translation_error_mm': float(np.linalg.norm(fit[3:]-translation)*1000),
            'reference_error_mm': float(np.linalg.norm(reference-[0, .02, -.60])*1000),
            'probe_eye_error_mm_median_max': np.percentile(errors, [50, 100]).tolist(),
            'train_residual_deg_median_p95': np.percentile(degrees(predicted(fit, targets, observed_q, 1.), gaze), [50, 95]).tolist(),
            'held_pose_residual_deg_median_p95': np.percentile(degrees(predicted(fit, held_targets, perturb_eyes(held_q, scenario), 1.), held_gaze), [50, 95]).tolist(),
            'alternative_rotation_cost_ratio': min(different)/solutions[0][0] if different and solutions[0][0] > 1e-20 else None}


def identifiability_checks():
    r, t, p = scene('top')
    targets = np.array([[x, y, 0] for y in [-.1, 0, .1] for x in [-.18, 0, .18]])
    q = np.array([.01, -.02, -.6]) @ r.T+t
    eyes = np.tile(q, (len(targets), 1))
    gaze = predicted(p, targets, eyes, 1.)
    b = cv2.Rodrigues(np.array([0., np.deg2rad(5), 0.]))[0]
    alternate = np.r_[cv2.Rodrigues(b@r)[0].ravel(), q+b@(t-q)]
    exact = float(np.max(np.abs(predicted(alternate, targets, eyes, 1.)-gaze@b.T)))
    assert exact < 1e-12, exact
    scale_alternate = p.copy(); scale_alternate[3:] += .1*q
    assert np.max(np.abs(predicted(scale_alternate, targets, eyes, 1.1)-gaze)) < 1e-12
    moving = np.array([q, q+[.1, 0, 0], q+[0, 0, .1]])
    error = np.linalg.norm(camera_to_screen(alternate, moving, 1.)-camera_to_screen(p, moving, 1.), axis=1)*1000
    assert error.max() > 8
    for placement in ('top', 'left', 'right'):
        result = one(placement, 'diverse', 'clean', 7)
        assert result['probe_eye_error_mm_median_max'][1] < .01, result
    return {'rotation_bias_exact_fit_max_chord_error': exact,
            'rotation_bias_5deg_10cm_motion_eye_error_mm': float(error.max()),
            'checks': 'PASS exact bias ambiguity, fixed-eye scale ambiguity, clean multi-start recovery for 3 placements'}


def scale_control():
    """Local oracle-initialized control, not a deployable new calibration solver."""
    r, t, truth = scene('top')
    targets = np.tile([[x, y, 0] for y in [-.1, 0, .1] for x in [-.18, 0, .18]], (3, 1))
    eyes = np.repeat([[0, .02, -.60], [.10, .07, -.5], [-.10, -.03, -.75]], 9, axis=0) @ r.T+t
    observed = eyes*1.1
    gaze = predicted(truth, targets, eyes, 1.)
    expected = np.r_[truth, np.log(1/1.1)]
    initial = expected+np.array([.03, -.04, .02, .02, -.02, .03, .05])
    fitted = fit_local_scale(targets, observed, gaze, initial)
    singular = np.linalg.svd(scale_jacobian(expected, targets, observed), compute_uv=False)
    assert np.max(np.abs(fitted-expected)) < 1e-5
    return {'scale_expected': 1/1.1, 'scale_fitted': float(np.exp(fitted[6])),
            'smallest_jacobian_singular_value': float(singular[-1]),
            'limitation': 'noiseless, known axes, near-truth initialization; does not solve unknown gaze bias or global initialization'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    checks = identifiability_checks()
    results = []
    for placement in ('top', 'left', 'right'):
        for motion in ('fixed', 'small', 'diverse'):
            for scenario in ('clean', 'noise_1', 'noise_3', 'bias_5deg', 'horizontal_gain_10pct',
                             'eye_scale_10pct', 'eye_depth_10pct', 'eye_origin_1cm'):
                for seed in (7, 19, 43):
                    try: results.append(one(placement, motion, scenario, seed))
                    except ValueError as error:
                        results.append({'placement': placement, 'motion': motion, 'scenario': scenario,
                                        'seed': seed, 'failed': str(error)})
            print('Completed', placement, motion, flush=True)
    report = {'self_checks': checks, 'joint_scale_local_control': scale_control(), 'experiments': results,
              'limitations': ['finite synthetic placements; not arbitrary placement proof',
                              'known screen size, synthetic eye model and axes',
                              'perturbation magnitudes not fitted to real webcam data',
                              'no crop, occlusion, identity or illumination model',
                              'fixed scale solver; camera intrinsics error only approximated by depth perturbation']}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False), encoding='utf-8')
    print(checks); print('Report:', args.output)


if __name__ == '__main__': main()

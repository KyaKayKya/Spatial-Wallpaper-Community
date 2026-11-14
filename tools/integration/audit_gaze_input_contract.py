"""No-camera numerical comparison of sampler preprocessing with official transforms.

Reference dependencies use the existing isolated export environment. The current
Windows DPI query creates no window and does not validate historical rendering.
"""
import argparse
import ast
import ctypes
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT/'Validation/Gaze/local'


def reference(path):
    import torch
    from torchvision import transforms
    transform = transforms.Compose([transforms.ToPILImage(), transforms.Resize(448), transforms.ToTensor(),
                                    transforms.Normalize([.485, .456, .406], [.229, .224, .225])])
    data = np.load(path)
    result = np.stack([transform(x).numpy() for x in data['crops']])
    logits = data['logits']
    angles = (torch.softmax(torch.from_numpy(logits), dim=1)*torch.arange(90)).sum(dim=1)*4-180
    np.savez(path.with_name('input-reference.npz'), normalized=result, angles=angles.numpy())


def main():
    import cv2
    from PIL import Image
    from solve_gaze_candidate import gaze_directions
    source = ROOT/'tools/integration/sample_gaze_spike.py'
    tree = ast.parse(source.read_text(encoding='utf-8'))
    assignments = {node.targets[0].id: node for node in ast.walk(tree)
                   if isinstance(node, ast.Assign) and isinstance(node.targets[0], ast.Name)
                   and node.targets[0].id in ('crop', 'resized', 'normalized')}
    assert len(assignments) == 3
    # Execute the actual sampler expressions rather than a second copy of them.
    body = ast.Module(body=[assignments[n] for n in ('crop', 'resized', 'normalized')], type_ignores=[])
    code = compile(body, str(source), 'exec')
    crops, normalized = [], []
    for seed in (1, 7, 19, 43):
        rgb = np.random.default_rng(seed).integers(0, 256, (480, 640, 3), dtype=np.uint8)
        env = {'cv2': cv2, 'np': np, 'Image': Image, 'rgb': rgb, 'x0': 103, 'x1': 389, 'y0': 57, 'y1': 421}
        exec(code, env)
        # Same selected ROI passed through official BGR -> RGB path.
        official_rgb = cv2.cvtColor(rgb[:, :, ::-1][57:421, 103:389], cv2.COLOR_BGR2RGB)
        np.testing.assert_array_equal(official_rgb, rgb[57:421, 103:389])
        crops.append(env['crop']); normalized.append(env['normalized'].transpose(2, 0, 1))
    logits = np.random.default_rng(3).normal(0, 3, (8, 90)).astype(np.float32)
    path = LOCAL/'input-cases.npz'; np.savez(path, crops=crops, logits=logits)
    subprocess.run([str(LOCAL/'.venv/Scripts/python.exe'), str(Path(__file__).resolve()), '--reference', str(path)], check=True)
    ref = np.load(LOCAL/'input-reference.npz')
    tensor_error = float(np.max(np.abs(np.array(normalized)-ref['normalized'])))
    a = logits.astype(float); probabilities = np.exp(a-a.max(axis=1, keepdims=True)); probabilities /= probabilities.sum(axis=1, keepdims=True)
    angle_error = float(np.max(np.abs(probabilities@np.arange(90)*4-180-ref['angles'])))
    assert tensor_error < 1e-6 and angle_error < 1e-4
    angles = np.array([[0, 0], [30, 0], [0, 30], [-20, 15]], dtype=float)
    radians = np.deg2rad(angles)
    upstream = np.column_stack((-np.cos(radians[:, 1])*np.sin(radians[:, 0]),
                                -np.sin(radians[:, 1]), -np.cos(radians[:, 1])*np.cos(radians[:, 0])))
    direction_error = float(np.max(np.abs(gaze_directions(angles)-upstream*[1, -1, 1])))
    assert direction_error < 1e-12
    user = ctypes.WinDLL('user32', use_last_error=True)
    user.GetThreadDpiAwarenessContext.restype = ctypes.c_void_p
    user.GetAwarenessFromDpiAwarenessContext.argtypes = [ctypes.c_void_p]
    success = bool(user.SetProcessDPIAware())
    awareness = int(user.GetAwarenessFromDpiAwarenessContext(user.GetThreadDpiAwarenessContext()))
    dpi = int(user.GetDpiForSystem())
    report = {'sampler_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
              'reference': 'official L2CS pipeline.py/utils.py a4d8f7fa5436a2b2b9f088471623b552a85811bd',
              'normalized_tensor_max_abs_error': tensor_error, 'softmax_angle_max_abs_error_deg': angle_error,
              'direction_conversion_max_abs_error': direction_error,
              'current_process_dpi': {'set_system_aware_succeeded': success, 'awareness': awareness,
                                      'system_dpi': dpi, 'primary_screen_px': [user.GetSystemMetrics(0), user.GetSystemMetrics(1)]},
              'crop_contract_difference': 'RetinaFace bbox upstream versus MediaPipe landmark bounds +10% in sampler',
              'limits': ['identical ROI synthetic images only; no real gaze accuracy claim',
                         'current DPI query, no historical or rendered pixel verification',
                         'official mathematical direction agreement is not physical ground truth']}
    (LOCAL/'input-contract-audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path)
    args = parser.parse_args()
    if args.reference: reference(args.reference)
    else: main()

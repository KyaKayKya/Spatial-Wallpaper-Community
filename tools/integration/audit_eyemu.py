"""No-camera EyeMU contract audit, using pinned upstream JS and ONNX reference.

Run with the existing Tracker Python. --reference is an internal subprocess
using the isolated ONNX environment; neither mode captures images.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT / 'Validation/Gaze/local'
SOURCE = LOCAL / 'uniteye-source'
SHA = '0cbbc9bfbb0d0b0b95b57deec3095e5d5ac81996'
MODEL = SOURCE / 'webgl/models/eyemu.onnx'
MODEL_HASH = '8b5a98b3242f4e6ed67b6dfb8123efd96cb801e646a2c42f3b4ed91a955053a5'
INPUTS = ['input_1:0', 'input_2:0', 'input_4', 'input_5']
OUTPUTS = ['dense_7', 'dense_8', 'dense_6']


def reference():
    import onnx
    from onnx.reference import ReferenceEvaluator
    model = onnx.load(str(MODEL))
    onnx.checker.check_model(model)
    runner = ReferenceEvaluator(model)
    data = np.load(LOCAL / 'eyemu-inputs.npz')
    result = {}
    for i in range(data['input_1:0'].shape[0]):
        out = runner.run(OUTPUTS, {n: data[n][i:i+1] for n in INPUTS})
        for n, value in zip(OUTPUTS, out):
            result.setdefault(n, []).append(value[0])
    np.savez(LOCAL / 'eyemu-reference.npz', **{k: np.array(v) for k, v in result.items()})
    def shape(v):
        return [d.dim_value or d.dim_param for d in v.type.tensor_type.shape.dim]
    metadata = {'inputs': {v.name: shape(v) for v in model.graph.input},
                'outputs': {v.name: shape(v) for v in model.graph.output},
                'onnx_version': onnx.__version__,
                'ops': sorted({n.op_type for n in model.graph.node})}
    (LOCAL / 'eyemu-graph.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')


def main():
    import cv2
    actual = subprocess.check_output(['git', '-C', str(SOURCE), 'rev-parse', 'HEAD'], text=True).strip()
    assert actual == SHA, 'Upstream changed; review contract before rerunning.'
    subprocess.run(['git','-C',str(SOURCE),'diff','--exit-code','HEAD','--','webgl','uniteye/Scripts','uniteye/Shaders'],check=True,stdout=subprocess.DEVNULL)
    assert hashlib.sha256(MODEL.read_bytes()).hexdigest() == MODEL_HASH
    assert hashlib.sha256((LOCAL/'eyemu-native.onnx').read_bytes()).hexdigest() == MODEL_HASH
    fixture_path = LOCAL / 'eyemu-upstream-fixture.json'
    subprocess.run(['node', str(Path(__file__).with_name('audit_eyemu_reference.cjs')),
                    str(SOURCE), str(fixture_path)], check=True)
    fixture = json.loads(fixture_path.read_text())
    lm = fixture['landmarks']
    # Independent conversion of Unity bottom-left crop geometry to top-left.
    for case in fixture['crops']:
        for key, a, b in [('left', 362, 263), ('right', 33, 133)]:
            l, r = lm[a], lm[b]
            width = r['x'] - l['x']
            side = int(width * 1.4 * case['w'])
            bottom = int(np.clip(1-(l['y']+r['y'])/2-width*1.4, 0, 1)*case['h'])
            expected = {'x': int((l['x']-.2*width)*case['w']),
                        'y': case['h']-bottom-side, 'size': side}
            assert case[key] == expected, (case, expected)
    yy, xx = np.mgrid[:128, :128]
    rgb = np.stack([xx*2, yy*2, (xx+yy)%256], axis=-1).astype(np.uint8)
    # Compare all packed RGB values, avoiding a test that only checks grayscale.
    expected = (rgb.astype(np.float64)/255-.5).astype(np.float32)
    got = np.array(fixture['tensor'], np.float32).reshape(128,128,3)
    np.testing.assert_array_equal(got, expected)
    pose = np.array(fixture['pose'], np.float32)
    f = fixture['features']
    expected_features = [1,2,3,4,.3,.7,.09,.49,.21,.027,.343,*pose]
    for a,b,iris in [(362,263,473),(33,133,468)]:
        p=np.array([lm[a]['x'],lm[a]['y']]); q=np.array([lm[b]['x'],lm[b]['y']])
        center=np.array([lm[iris]['x'],lm[iris]['y']])
        expected_features.extend((center-(p+q)/2)/np.linalg.norm(q-p))
    expected_features.extend([.03,-.02,.6,.1,.2,.3,.4,.5,.6,.7,.8,
                              .3*pose[0],.7*pose[1],.009,-.014,.18,.42])
    np.testing.assert_allclose(f,expected_features,rtol=1e-6,atol=1e-7)
    # Synthetic inputs test numerical compatibility, never gaze accuracy.
    corners=np.array([lm[i][axis] for i in [263,362,33,133] for axis in ['x','y']],np.float32)
    rng=np.random.default_rng(19)
    eyes=np.stack([np.zeros_like(got),got,got[:,::-1],got[:,:,::-1],
                   rng.uniform(-.5,.5,got.shape).astype(np.float32)])
    feeds={'input_1:0':eyes,'input_2:0':eyes[:,::-1].copy(),
           'input_4':np.tile(corners,(len(eyes),1)),
           'input_5':np.tile(pose,(len(eyes),1))}
    np.savez(LOCAL/'eyemu-inputs.npz',**feeds)
    subprocess.run([str(LOCAL/'.venv/Scripts/python.exe'),str(Path(__file__).resolve()),'--reference'],check=True)
    ref=np.load(LOCAL/'eyemu-reference.npz')
    net=cv2.dnn.readNetFromONNX(str(MODEL))
    net.setPreferableBackend(cv2.dnn.DNN_BACKEND_OPENCV)
    net.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)
    cv2.setNumThreads(2)
    errors={n:0. for n in OUTPUTS}
    for i in range(len(eyes)):
        for n in INPUTS: net.setInput(feeds[n][i:i+1],n)
        for n,out in zip(OUTPUTS,net.forward(OUTPUTS)):
            assert np.isfinite(out).all()
            np.testing.assert_allclose(out[0],ref[n][i],rtol=1e-4,atol=1e-4)
            errors[n]=max(errors[n],float(np.max(np.abs(out[0]-ref[n][i]))))
    for _ in range(5): net.forward(OUTPUTS)
    times=[]
    for _ in range(30):
        start=time.perf_counter();net.forward(OUTPUTS);times.append((time.perf_counter()-start)*1000)
    report={'source_commit':SHA,'model_sha256':MODEL_HASH,'model_bytes':MODEL.stat().st_size,
            'graph':json.loads((LOCAL/'eyemu-graph.json').read_text()),
            'opencv_version':cv2.__version__,'synthetic_cases':len(eyes),
            'backend_note':'OpenCV backend requested; OpenCV 5 warns target selection is unsupported by its new graph engine. No GPU benchmark performed.',
            'reference_max_abs_error':errors,'rgb_tensor_max_abs_error':float(np.max(np.abs(got-expected))),
            'crop_cases':fixture['crops'],'checks':fixture['checked']+['36_features','rgb_nhwc_normalization','crop_origin_conversion'],
            'original_eyemu_commit':fixture['originalSha'],'original_crop_comparison':fixture['cropComparison'],
            'native_and_web_weights_identical':True,
            'cpu_model_only_2_threads_ms':{'median':float(np.median(times)),'p95':float(np.percentile(times,95))},
            'camera_opened':False,
            'limits':['No real eyes or gaze accuracy tested.','Canvas calls mocked; no browser/Unity GPU resampling equivalence claim.',
                      'Native default matrix pose/smoothed landmarks differ from Web legacy pose/raw landmarks.',
                      'Model outputs are screen regression, not physical gaze rays.']}
    (LOCAL/'eyemu-contract-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference',action='store_true')
    args=parser.parse_args()
    reference() if args.reference else main()

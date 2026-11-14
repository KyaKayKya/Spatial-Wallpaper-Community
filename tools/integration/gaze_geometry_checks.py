"""Synthetic observability checks; no camera and no physical-accuracy claim."""
import json
from pathlib import Path

import cv2
import numpy as np

def directions(parameters, points, eyes):
    rotation=cv2.Rodrigues(parameters[:3])[0]
    vectors=points@rotation.T+parameters[3:6]-np.exp(parameters[6])*eyes
    lengths=np.linalg.norm(vectors,axis=1)
    if np.any(lengths<1e-8): raise ValueError('Target coincides with eye')
    return vectors/lengths[:,None]

def jacobian(parameters, points, eyes):
    columns=[]
    for i in range(7):
        offset=np.zeros(7); offset[i]=1e-5
        columns.append(((directions(parameters+offset,points,eyes)-directions(parameters-offset,points,eyes))/2e-5).ravel())
    return np.array(columns).T

def fit(points,eyes,gaze,initial):
    """Local numerical experiment; initial must already be in the solution basin."""
    p=np.array(initial,dtype=float); damping=1e-3
    for _ in range(120):
        residual=(directions(p,points,eyes)-gaze).ravel()
        j=jacobian(p,points,eyes)
        step=np.linalg.solve(j.T@j+damping*np.eye(7),-j.T@residual)
        candidate=p+step
        if abs(candidate[6])>5:
            damping*=10; continue
        next_residual=(directions(candidate,points,eyes)-gaze).ravel()
        if next_residual@next_residual<residual@residual:
            p=candidate; damping=max(1e-10,damping/3)
            if np.linalg.norm(step)<1e-9:break
        else:damping*=10
    return p

def main():
    grid=np.array([[x,y,0] for x in [-.4,0,.4] for y in [-.22,0,.22]])
    eyes=np.repeat([[0,0,.65],[.12,.03,.65],[-.08,-.05,.5]],9,axis=0)
    points=np.tile(grid,(3,1))
    truth=np.array([.1,3.0,-.15,.03,.12,.02,np.log(1.7)])
    expected=directions(truth,points,eyes)
    initial=truth+np.array([.03,-.08,.04,.02,-.03,.04,.08])
    solution=fit(points,eyes,expected,initial)
    error=float(np.max(np.abs(solution-truth)))
    assert error<1e-5, error
    multi_singular=np.linalg.svd(jacobian(truth,points,eyes),compute_uv=False)
    fixed_eyes=np.tile(eyes[0],(len(points),1))
    fixed_singular=np.linalg.svd(jacobian(truth,points,fixed_eyes),compute_uv=False)
    assert fixed_singular[-1]<1e-7
    assert multi_singular[-1]>1e-3
    # Preserve the ray field at one eye despite a wrong scale and translation.
    other=truth.copy(); old_scale=np.exp(truth[6]); other[6]=np.log(old_scale*1.3)
    other[3:6]+=(np.exp(other[6])-old_scale)*eyes[0]
    assert np.max(np.abs(directions(other,points,fixed_eyes)-directions(truth,points,fixed_eyes)))<1e-12
    shifted_error=np.linalg.norm(directions(other,points,eyes)-expected,axis=1)
    assert np.max(shifted_error)>.01
    report={'checks':'PASS: local noiseless recovery; fixed-eye scale degeneracy; multi-eye rank; held-out pose exposes scale error', 'max_parameter_error':error,'singular_values_multi_eye':multi_singular.tolist(),'singular_values_fixed_eye':fixed_singular.tolist(),'max_ray_chord_error_wrong_scale_new_pose':float(np.max(shifted_error)),'limitations':'synthetic local solve only, known coordinate convention and near-truth initialization; not robust global initialization, no model/real-camera accuracy'}
    out=Path(__file__).resolve().parents[2]/'Validation/Gaze/local'
    out.mkdir(parents=True,exist_ok=True)
    (out/'geometry-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))
if __name__=='__main__':main()

"""Experimental fixed-scale screen pose fit; never writes production presets.

Uses same-frame eyes and gaze, nine training targets and four held-out targets.
Screen width is nominal, not measured. This produces a preview, not acceptance.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'tracker/src'))
from spatial_tracker.estimator import ESTIMATOR_ID, HEAD_MODEL_ID


def gaze_directions(angles_deg):
    # Official gazeto3d / draw_gaze: image x-right,y-down,z-away from camera.
    # Convert y-down to Tracker C' y-up; angles are head0,head1, not Euler head pose.
    horizontal, vertical = np.deg2rad(np.asarray(angles_deg)).T
    return np.column_stack((-np.cos(vertical)*np.sin(horizontal),
                            np.sin(vertical),-np.cos(vertical)*np.cos(horizontal)))


def predicted(parameters, targets, eyes, scale):
    rotation = cv2.Rodrigues(parameters[:3])[0]
    vectors = targets@rotation.T + parameters[3:] - scale*eyes
    return vectors/np.maximum(np.linalg.norm(vectors,axis=1,keepdims=True),1e-12)


def fit_pose(targets, eyes, gaze, weights, scale, initial):
    parameters = np.array(initial,dtype=float)
    damping = 1e-3
    def residual(p): return predicted(p,targets,eyes,scale)-gaze
    def cost(r):
        norm = np.linalg.norm(r,axis=1)
        delta = .05
        return np.sum(weights*np.where(norm<=delta,.5*norm**2,delta*(norm-.5*delta)))
    for _ in range(100):
        r = residual(parameters)
        robust = np.sqrt(weights*np.minimum(1,.05/np.maximum(np.linalg.norm(r,axis=1),1e-12)))
        columns = []
        for axis in range(6):
            offset = np.zeros(6); offset[axis] = 1e-5
            columns.append(((residual(parameters+offset)-residual(parameters-offset))*robust[:,None]/2e-5).ravel())
        jac = np.array(columns).T
        step = np.linalg.solve(jac.T@jac+damping*np.eye(6),-jac.T@(r*robust[:,None]).ravel())
        trial = parameters+step
        if np.linalg.norm(trial[3:])<3 and cost(residual(trial))<cost(r):
            parameters = trial; damping = max(damping/3,1e-9)
            if np.linalg.norm(step)<1e-8: break
        else: damping = min(damping*10,1e10)
    return parameters,float(cost(residual(parameters)))


def camera_to_screen(parameters, eyes, scale):
    rotation = cv2.Rodrigues(parameters[:3])[0]
    return (scale*np.asarray(eyes)-parameters[3:])@rotation


def quaternion(rotation):
    vector = cv2.Rodrigues(rotation)[0].ravel()
    angle = np.linalg.norm(vector)
    return np.r_[vector/angle*np.sin(angle/2),np.cos(angle/2)] if angle>1e-12 else np.array([0.,0.,0.,1.])


def fit_multistart(targets,eyes,gaze,weights,scale):
    solutions = []
    for yaw in (-.8,0,.8):
        rotation = cv2.Rodrigues(np.array([0.,np.pi+yaw,0.]))[0]
        for distance in (.4,.7,1.):
            translation = scale*np.mean(eyes,axis=0)+np.mean(gaze,axis=0)*distance-np.mean(targets,axis=0)@rotation.T
            result,cost = fit_pose(targets,eyes,gaze,weights,scale,np.r_[cv2.Rodrigues(rotation)[0].ravel(),translation])
            mapped = camera_to_screen(result,eyes,scale)
            if np.all(mapped[:,2]<-.05) and np.all(np.linalg.norm(mapped,axis=1)<3):
                solutions.append((cost,result))
    if not solutions: raise ValueError('No safe front-of-screen solution; do not launch')
    solutions.sort(key=lambda item:item[0])
    return solutions[0][1],solutions


def evaluate(parameters, targets, eyes, gaze, scale, width, pixels):
    rotation = cv2.Rodrigues(parameters[:3])[0]
    mapped = camera_to_screen(parameters,eyes,scale)
    rays = gaze@rotation
    distance = -mapped[:,2]/np.where(np.abs(rays[:,2])>1e-8,rays[:,2],np.nan)
    intersections = mapped + rays*distance[:,None]
    errors = np.linalg.norm(intersections[:,:2]-targets[:,:2],axis=1)/width*pixels
    angles = np.rad2deg(np.arccos(np.clip(np.sum(predicted(parameters,targets,eyes,scale)*gaze,axis=1),-1,1)))
    safe = bool(np.all(np.isfinite(errors)) and np.all(distance>0))
    return {'positive_intersections':safe,'ray_model_residual_deg_median_p95':np.percentile(angles,[50,95]).tolist(),
            'screen_residual_px_median_p95':np.percentile(errors,[50,95]).tolist() if safe else None,
            'sample_count':len(eyes)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('session',type=Path)
    parser.add_argument('--monitor',nargs=4,type=int,required=True,metavar=('LEFT','TOP','WIDTH','HEIGHT'))
    parser.add_argument('--screen-width',type=float,default=.53)
    parser.add_argument('--scale',type=float,default=1.)
    args = parser.parse_args()
    if args.screen_width<=0 or not 0<args.scale<=10 or min(args.monitor[2:])<=0: parser.error('Invalid dimensions or scale')
    records = [json.loads(line) for line in args.session.read_text(encoding='utf-8').splitlines()]
    metadata = records[0]
    rows = [row for row in records if row['type']=='sample' and row['valid']]
    latest = {i:max(r.get('attempt',0) for r in records if r['type']=='sample' and r['target_index']==i) for i in range(13)}
    rows = [row for row in rows if row.get('attempt',0)==latest[row['target_index']]]
    counts = {i:sum(r['target_index']==i for r in rows) for i in range(13)}
    if any(counts[i]<5 for i in range(9)): raise ValueError('Need >=5 valid samples at every training target')
    left,top,pixel_width,pixel_height = args.monitor
    xy = np.array([r['target_desktop_px'] for r in rows])
    if np.any(xy[:,0]<left) or np.any(xy[:,0]>=left+pixel_width) or np.any(xy[:,1]<top) or np.any(xy[:,1]>=top+pixel_height):
        raise ValueError('Targets do not belong to the specified monitor')
    targets = np.column_stack(((xy[:,0]-left-pixel_width/2)*args.screen_width/pixel_width,
                               (top+pixel_height/2-xy[:,1])*args.screen_width/pixel_width,np.zeros(len(rows))))
    eyes = np.array([r['eye_position'] for r in rows])
    gaze = gaze_directions([r['pitch_yaw_deg_upstream'] for r in rows])
    train = np.array([r['split']=='fit' for r in rows])
    weights = np.array([1/counts[r['target_index']] for r in rows])
    parameters,solutions = fit_multistart(targets[train],eyes[train],gaze[train],weights[train],args.scale)
    rotation = cv2.Rodrigues(parameters[:3])[0]
    q0 = np.mean(eyes[train],axis=0)
    e0 = camera_to_screen(parameters,q0,args.scale)
    mapped = camera_to_screen(parameters,eyes,args.scale)
    if not np.all(mapped[:,2]<-.05) or not np.all(np.linalg.norm(mapped,axis=1)<3): raise ValueError('Unsafe sampled eye mapping')
    q = quaternion(rotation.T)
    mode = metadata['mode']; w,h = mode['width_px'],mode['height_px']
    mapping = {'camera_id':metadata['camera_id'],'head_model_id':HEAD_MODEL_ID,'estimator_id':ESTIMATOR_ID,
               'intrinsics_id':f'prior-fx-width-{w}x{h}-v1','geometry_id':f'upright-full-{w}x{h}-v1',
               'coordinate_frame':'camera_upright_v1','length_unit':'model_m','scale_source':'canonical_face_prior',
               'q0':q0.tolist(),'e0':e0.tolist(),'q_xyzw':q.tolist(),'scale':args.scale,'view_center_m':[0,0,0],'measured':False}
    report = {'schema_version':1,'experimental':True,'source_session':str(args.session.resolve()),
              'source_sha256':hashlib.sha256(args.session.read_bytes()).hexdigest(),
              'placement':metadata['placement'],'monitor_px':args.monitor,'nominal_screen_width':args.screen_width,
              'scale_fixed_prior':args.scale,'ray_origin_assumption':'same-frame canonical binocular eye center; not independently validated against Gaze360 labels',
              'R_screen_to_camera':rotation.tolist(),'t_screen_to_camera':parameters[3:].tolist(),
              'mapping':mapping,'eye_span_model_m':np.ptp(eyes,axis=0).tolist(),
              'train':evaluate(parameters,targets[train],eyes[train],gaze[train],args.scale,args.screen_width,pixel_width),
              'held_out':evaluate(parameters,targets[~train],eyes[~train],gaze[~train],args.scale,args.screen_width,pixel_width),
              'multistart_costs':[cost for cost,_ in solutions],
              'multistart_solutions':[{'cost':cost,'reference_eye':camera_to_screen(p,q0,args.scale).tolist(),
                  'R_screen_to_camera':cv2.Rodrigues(p[:3])[0].tolist(),'t_screen_to_camera':p[3:].tolist()}
                  for cost,p in solutions],'scale_sensitivity':[],
              'accepted_calibration':False,'limitations':['nominal screen size and fixed scale','gaze origin assumption','unknown true intrinsics','no independent geometry ground truth']}
    for scale in (args.scale*.75,args.scale*1.25):
        alternative,_ = fit_multistart(targets[train],eyes[train],gaze[train],weights[train],scale)
        report['scale_sensitivity'].append({'scale':scale,'reference_eye':camera_to_screen(alternative,q0,scale).tolist(),
            'rotation_change_deg':float(np.linalg.norm(cv2.Rodrigues(cv2.Rodrigues(alternative[:3])[0].T@rotation)[0])*180/np.pi)})
    output = ROOT/'Validation/Gaze/local'/('candidate-'+hashlib.sha256(args.session.read_bytes()).hexdigest()[:12])
    output.mkdir(parents=True,exist_ok=True)
    (output/'candidate.json').write_text(json.dumps(report,indent=2,allow_nan=False),encoding='utf-8')
    print(json.dumps(report,indent=2)); print('Candidate directory:',output)


if __name__=='__main__': main()

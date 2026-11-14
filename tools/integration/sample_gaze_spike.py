"""Local numeric-only gaze experiment; not a production calibration interface.

Close the running calibration demo before starting this standalone camera owner.
SPACE captures a target after one second to settle; ESC ends the session.
"""
import argparse
import asyncio
import ctypes
import hashlib
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT / 'Validation/Gaze/local'
sys.path.insert(0, str(ROOT / 'tracker/src'))
from spatial_tracker.assets import model_paths
from spatial_tracker.camera import WindowsCamera
from spatial_tracker.clock import qpc_us
from spatial_tracker.estimator import LANDMARKS, load_head_model, solve_pose

# Whole targets, and separately whole head positions, must be held out in analysis.
TARGETS = [(x, y, 'fit') for y in (.15, .5, .85) for x in (.15, .5, .85)]
TARGETS += [(x, y, 'held_out') for x, y in ((.3,.3),(.7,.3),(.3,.7),(.7,.7))]


def safe_angles(angles):
    return angles if np.isfinite(angles).all() else None


def target_summary(total, valid):
    return {'sample_count':total,'valid_count':valid,'usable':valid>=5}


class Inference:
    def __init__(self, model_root, threads):
        import mediapipe as mp
        self.mp = mp
        paths = model_paths(model_root)
        self.points = load_head_model(paths['canonical_face_model.obj'])
        self.eye = self.points[[33, 133, 263, 362]].mean(axis=0)
        options = mp.tasks.vision.FaceLandmarkerOptions(
            base_options=mp.tasks.BaseOptions(model_asset_path=str(paths['face_landmarker.task'])),
            running_mode=mp.tasks.vision.RunningMode.IMAGE, num_faces=1)
        self.face = mp.tasks.vision.FaceLandmarker.create_from_options(options)
        model = LOCAL / 'l2cs_net_1x3x448x448.onnx'
        report = json.loads((LOCAL / 'export-report.json').read_text())
        if hashlib.sha256(model.read_bytes()).hexdigest() != report['onnx_sha256']:
            raise ValueError('ONNX hash mismatch')
        cv2.setNumThreads(threads)
        self.net = cv2.dnn.readNetFromONNX(str(model))
        self.net.setPreferableBackend(cv2.dnn.DNN_BACKEND_OPENCV)
        self.net.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)

    def process(self, rgb):
        start = time.perf_counter()
        result = self.face.detect(self.mp.Image(image_format=self.mp.ImageFormat.SRGB,
                                              data=np.ascontiguousarray(rgb)))
        row = {'flags': [], 'valid': False}
        if not result.face_landmarks:
            row['flags'].append('no_face')
        else:
            height, width = rgb.shape[:2]
            face = result.face_landmarks[0]
            xy = np.array([(p.x * width, p.y * height) for p in face])
            # MediaPipe region replaces RetinaFace: accuracy must be revalidated.
            lo, hi = xy[:468].min(axis=0), xy[:468].max(axis=0)
            pad = (hi-lo)*.1
            raw = np.r_[np.floor(lo-pad), np.ceil(hi+pad)].astype(int)
            box = np.maximum([0,0,0,0], np.minimum([width,height,width,height], raw))
            x0,y0,x1,y1 = box.tolist()
            row['roi_xyxy'] = box.tolist()
            if not np.array_equal(raw, box): row['flags'].append('clipped_face')
            try:
                pose, eye, rms = solve_pose(self.points[LANDMARKS], xy[LANDMARKS],
                                           width, height, self.eye)
                row.update(head_pose=pose, eye_position=eye, reprojection_error_px=rms)
                if rms > max(8, width*.02): row['flags'].append('low_geometry_quality')
            except (ValueError, cv2.error):
                row['flags'].append('invalid_pose')
            if min(x1-x0, y1-y0) < 32:
                row['flags'].append('small_face')
            else:
                crop = cv2.resize(rgb[y0:y1,x0:x1], (224,224))
                resized = np.asarray(Image.fromarray(crop).resize((448,448), Image.Resampling.BILINEAR))
                normalized = (resized.astype(np.float32)/255 - np.array([.485,.456,.406],np.float32)) / np.array([.229,.224,.225],np.float32)
                self.net.setInput(np.ascontiguousarray(normalized.transpose(2,0,1)[None]))
                outputs = self.net.forward(['head0','head1'])
                angles = []
                for logits in outputs:
                    logits = logits.reshape(90).astype(np.float64)
                    probabilities = np.exp(logits-logits.max()); probabilities /= probabilities.sum()
                    angles.append(float(probabilities @ np.arange(90)*4-180))
                row['pitch_yaw_deg_upstream'] = safe_angles(angles)
                if row['pitch_yaw_deg_upstream'] is None: row['flags'].append('nonfinite_gaze')
                # Preserve raw angles; camera-basis signs and ray origin are not verified yet.
                row['valid'] = not row['flags']
        row['inference_ms'] = (time.perf_counter()-start)*1000
        return row

    def close(self):
        self.face.close()


async def run(args):
    engine = Inference(args.models, args.threads)
    if args.smoke:
        try:
            result = await asyncio.to_thread(engine.process, np.zeros((480,640,3),np.uint8))
            assert result['flags'] == ['no_face'] and not result['valid']
            print(json.dumps(result))
        finally:
            engine.close()
        return
    if not args.camera or not args.placement or not args.pose:
        engine.close()
        raise ValueError('--camera, --placement and --pose are required for participant sampling')
    ctypes.windll.user32.SetProcessDPIAware()
    camera = WindowsCamera()
    latest, errors = [None], []
    def frame(rgb, stamp): latest[0] = (rgb, stamp)
    LOCAL.mkdir(parents=True, exist_ok=True)
    output = LOCAL / ('samples-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ') + '.jsonl')
    window = 'Gaze research - SPACE capture / ESC exit'
    width,height = 1000,650
    job = None
    try:
        mode = await camera.open(args.camera, frame, errors.append)
        cv2.namedWindow(window, cv2.WINDOW_AUTOSIZE)
        with output.open('x', encoding='utf-8') as log:
            metadata = {'type':'session','version':1,'placement':args.placement,'pose':args.pose,
                        'camera_id':args.camera,'mode':mode,'threads':args.threads,
                        'coordinate_frame':'camera_upright_v1','length_unit':'model_m',
                        'crop':'mediapipe-face-bounds-plus-10-percent-v1',
                        'model':json.loads((LOCAL/'export-report.json').read_text()),
                        'limitations':['no blink or blur rejection yet','gaze basis and origin unverified',
                                        'no independent geometry ground truth','window target coordinates only']}
            log.write(json.dumps(metadata)+'\n'); log.flush()
            index, capture_start, capture_end, locked_rect = 0,None,None,None
            counts = [0,0]
            attempt, retry = 0, False
            while index < len(TARGETS):
                if errors: raise errors[0]
                x,y,split = TARGETS[index]
                canvas = np.full((height,width,3),235,np.uint8)
                cv2.circle(canvas,(round(x*(width-1)),round(y*(height-1))),9,(40,80,220),-1)
                active = capture_start is not None
                label = f'{index+1}/13 {args.pose}: ' + ('Look at dot, keep still' if active else 'Look at dot, then SPACE')
                if retry and not active: label = 'Too few valid frames. Face camera, SPACE retries this dot'
                cv2.putText(canvas,label,(15,30),cv2.FONT_HERSHEY_SIMPLEX,.65,(20,20,20),1)
                cv2.imshow(window,canvas)
                key = cv2.waitKey(1)&255
                if key == 27 or cv2.getWindowProperty(window,cv2.WND_PROP_VISIBLE)<1: break
                rect = tuple(cv2.getWindowImageRect(window))
                if active and rect != locked_rect: raise RuntimeError('Window moved while sampling; restart this session')
                if key == 32 and not active:
                    capture_start = qpc_us()+1_000_000
                    capture_end = capture_start+2_000_000
                    locked_rect = rect
                    latest[0] = None
                    counts = [0,0]
                    attempt += 1
                if active and job is None and latest[0] is not None:
                    rgb, stamp = latest[0]; latest[0] = None
                    if capture_start <= stamp <= capture_end:
                        job = asyncio.create_task(asyncio.to_thread(engine.process,rgb))
                        job_stamp = stamp
                if job is not None and job.done():
                    row = job.result(); job = None
                    row.update(type='sample',target_index=index,attempt=attempt,split=split,
                               target_uv=[x,y],target_desktop_px=[rect[0]+x*(width-1),rect[1]+y*(height-1)],
                               window_rect=list(rect),captured_at_us=job_stamp,
                               processed_at_us=qpc_us(),target_stable_since_us=capture_start-1_000_000)
                    log.write(json.dumps(row,allow_nan=False)+'\n'); log.flush()
                    counts[0] += 1
                    counts[1] += int(row['valid'])
                if active and qpc_us()>capture_end and job is None:
                    summary = target_summary(*counts)
                    log.write(json.dumps({'type':'target_end','target_index':index,'attempt':attempt,**summary})+'\n'); log.flush()
                    retry = not summary['usable']
                    if not retry: index += 1
                    capture_start = capture_end = None
                await asyncio.sleep(.005)
            log.write(json.dumps({'type':'session_end','targets_completed':index,'complete':index==len(TARGETS)})+'\n')
        print('Numeric samples:',output)
    finally:
        await camera.close()
        if job is not None: await job
        engine.close()
        cv2.destroyAllWindows()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--camera'); parser.add_argument('--placement'); parser.add_argument('--pose')
    parser.add_argument('--models',type=Path,default=ROOT/'tracker/.cache/models')
    parser.add_argument('--threads',type=int,default=4)
    parser.add_argument('--smoke',action='store_true',help='Load models and check blank frame without camera or window')
    args = parser.parse_args()
    asyncio.run(run(args))


if __name__ == '__main__': main()

"""Check actual Player evidence; absent or incomplete acceptance is a failure."""
import json,csv
from pathlib import Path
root=Path(__file__).resolve().parents[2]
report=root/'Validation/Mita/capture/validation.json'
assert report.exists(),'Player has not produced multi-aspect validation evidence'
data=json.loads(report.read_text(encoding='utf8'))
assert len(data['geometry'])==70,'Expected 5 aspect ratios x 2 fits x 7 eye positions'
assert max(x['corner_error_px'] for x in data['geometry'])<.05
assert all(x['floor_covers_aperture'] and x['uniform_scale'] and x['fixed_camera_rotation'] and x['scaled_light_range'] for x in data['geometry'])
assert data['animation_seconds']>=45
assert data['extra_motion_visits']>=2
assert data['extra_motion_seconds']>=10
assert data['extra_expression_range']>.8
assert .8<data['blink_range']<1.1,'Random blink must not add over native gesture eyelids'
assert data['max_eye_error_degrees']<2
assert data['tracked_seconds']>20
assert data['character_root_drift']<.00001
assert data['calm_seconds']>31 and data['calm_unexpected_states']==0
assert data['body_bones']>=2 and .01<data['calm_body_rotation_degrees']<10 and data['calm_body_step_degrees']<1.5
assert data['touch_hit'] and data['touch_miss'] and data['dismiss_started']
assert data['click_effects']==2 and data['wink_effects']>=1
assert data['peak_click_particles']>0 and data['peak_wink_particles']>0 and data['click_effect_error_px']<.05
assert data['click_audio_playing'] and 0<data['click_audio_seconds']<1
print('PASS: geometry, animation, expressions, eye constraints and root stability')
print(json.dumps({k:v for k,v in data.items() if k!='geometry'},indent=2))
print('max corner error px:',max(x['corner_error_px'] for x in data['geometry']))
life=json.loads((report.parent/'lifecycle.json').read_text())
assert all(life[k] for k in ['received_before_pause','clock_frozen','tracking_released','audio_paused','received_after_resume','audio_time_frozen','audio_resumed']),life
assert life['audio_clip_seconds']>16,life
desktop=json.loads((report.parent/'desktop-proof.json').read_text())
assert desktop['attached'] and desktop['verified'] and desktop['detached'],desktop
print('PASS: tracking lifecycle and real Windows desktop attach/detach')
with (report.parent/'recording.csv').open() as stream:frames=list(csv.DictReader(stream))
assert len(frames)==480
assert abs(float(frames[-1]['time'])-16)<.1
assert max(float(row['cameraAngle']) for row in frames)<.00001
assert max(float(row['cornerErrorPx']) for row in frames)<.05
assert all(abs(float(row['eyeX']))<.00001 and abs(float(row['eyeY']))<.00001 for row in frames),'Default opening must not sweep the observation eye'
assert max(float(row['placementZ']) for row in frames)-min(float(row['placementZ']) for row in frames)>.1
assert max(float(row['placementYaw']) for row in frames)-min(float(row['placementYaw']) for row in frames)>8
settled=[row for row in frames if float(row['time'])>6.5]
for key in ['placementX','placementY','placementZ','placementYaw']:
 assert max(float(row[key]) for row in settled)-min(float(row[key]) for row in settled)<.00001,key
assert all(abs(float(row['faceYaw']))<8 for row in frames if 3.2<float(row['time'])<3.6)
assert all(35<float(row['faceYaw'])<50 for row in frames if 6.5<float(row['time'])<7.5)
with (report.parent/'gestures/audit.csv').open(encoding='utf-8-sig') as stream:gestures=list(csv.DictReader(stream))
assert len(gestures)==6 and all(row['returnedIdle']=='True' for row in gestures)
assert all(max(float(row['leftTravel']),float(row['rightTravel']))>.05 for row in gestures)
assert all(float(row['maxExtraFaceLayer'])==0 and abs(float(row['idleBlink']))<.01 for row in gestures),'Interrupting a blink must not leave eyelids closed after a gesture'
print('PASS: native character turn, original camera track, six gestures and 480-frame window invariants')

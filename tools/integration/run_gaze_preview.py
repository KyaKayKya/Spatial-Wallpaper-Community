"""Stage an experimental preset in a new data root; restore uses original demo data.

No L2CS inference runs during this preview. The existing Player owns MediaPipe.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]


def prepare(candidate_path):
    candidate = json.loads(candidate_path.read_text(encoding='utf-8'))
    if candidate.get('experimental') is not True or candidate.get('accepted_calibration') is not False:
        raise ValueError('Expected a non-accepted experimental candidate')
    if candidate['nominal_screen_width'] != .53:
        raise ValueError('Existing demo uses nominal 0.53 m monitor width')
    if not candidate['train']['positive_intersections']:
        raise ValueError('Invalid ray intersections')
    mapping = candidate['mapping']
    identifier = uuid.uuid4().hex
    run = ROOT/'Validation/Gaze/local'/('preview-'+identifier[:12])
    data = run/'data'; presets = data/'presets'; presets.mkdir(parents=True)
    original = Path(os.environ['USERPROFILE'])/'AppData/LocalLow/Spatial Wallpaper/Spatial Wallpaper Calibration/CalibrationDemo'
    preserved = []
    if original.exists():
        for source in original.rglob('*.json'):
            relative = source.relative_to(original)
            preserved.append({'path':str(relative),'sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
        shutil.copytree(original,run/'original-backup')
        # Existing presets remain selectable, but edits affect only the isolated copy.
        if (original/'presets').exists(): shutil.copytree(original/'presets',presets,dirs_exist_ok=True)
    placement_label={'right':'右侧','top':'顶部','initial':'左侧'}.get(candidate['placement'],candidate['placement'])
    profile = {'schema_version':1,'id':identifier,'name':'实验：'+placement_label+'相机（未验收）',
               'revision':1,'mapping':mapping,'world_pitch':0,'world_roll':0,
               'follow_yaw':0,'follow_pitch':0,'follow_roll':0,
               'base_q':mapping['q_xyzw'],'base_scale':mapping['scale'],'amplitude':1,
               'reference_ready':True,'display_units':'nominal_m'}
    profile_path = presets/(identifier+'.json')
    profile_path.write_text(json.dumps(profile,ensure_ascii=False,indent=2),encoding='utf-8')
    (data/'settings.json').write_text(json.dumps({'camera_id':mapping['camera_id'],
        'camera_name':'Full HD webcam','preset_id':identifier},ensure_ascii=False),encoding='utf-8')
    shutil.copy2(candidate_path,run/'candidate.json')
    manifest = {'data_root':str(data),'profile':str(profile_path),'original_root':str(original),
                'original_files':preserved,'candidate_sha256':hashlib.sha256(candidate_path.read_bytes()).hexdigest()}
    (run/'preview.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    return run,manifest


def verify_original(manifest):
    original = Path(manifest['original_root'])
    for record in manifest['original_files']:
        path = original/record['path']
        if not path.exists() or hashlib.sha256(path.read_bytes()).hexdigest()!=record['sha256']:
            raise RuntimeError('Original preset changed outside this isolated preview: '+str(path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument('--candidate',type=Path)
    modes.add_argument('--prepared',type=Path,help='Previously prepared preview directory')
    modes.add_argument('--restore',action='store_true',help='Open unchanged original demo after closing preview')
    parser.add_argument('--prepare-only',action='store_true')
    parser.add_argument('--diagnostics',action='store_true',help='Record numeric eye-to-projection diagnostics, never images')
    args = parser.parse_args()
    python = ROOT/'tracker/.venv/Scripts/python.exe'
    if not python.exists(): python = ROOT.parent.parent/'tracker/.venv/Scripts/python.exe'
    if args.restore:
        # Original settings and presets have never been overwritten.
        raise SystemExit(subprocess.call([str(python),str(ROOT/'tools/integration/run_calibration.py')]))
    if args.candidate:
        run,manifest = prepare(args.candidate.resolve())
    else:
        run = args.prepared.resolve()
        manifest = json.loads((run/'preview.json').read_text(encoding='utf-8'))
    verify_original(manifest)
    print('Experimental preview:',run,flush=True)
    print('Candidate profile:',manifest['profile'],flush=True)
    if args.prepare_only: return
    player = ROOT/'Prototype/Builds/Calibration/CalibrationDemo.exe'
    if not player.exists(): raise FileNotFoundError('Build calibration demo first')
    command = [str(player),'--data-root',manifest['data_root'],
               '--tracking-python',str(python),'--tracking-root',str(ROOT/'tracker'),
               '-logFile',str(run/'player.log')]
    if args.diagnostics:
        diagnostic_path=run/('diagnostics-'+uuid.uuid4().hex[:12]+'.csv')
        command += ['--calibration-diagnostics',str(diagnostic_path)]
        print('Numeric diagnostics:',diagnostic_path,flush=True)
    print('Open 展示柜体验, then 全屏体验. Do not recapture reference. ESC leaves fullscreen.',flush=True)
    result = subprocess.call(command,cwd=ROOT/'Prototype')
    verify_original(manifest)
    print('Original presets verified unchanged; Player exit',result,flush=True)
    raise SystemExit(result)


if __name__=='__main__': main()

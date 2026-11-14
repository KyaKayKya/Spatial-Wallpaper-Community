"""Export, build, open and validate the MiniShotGame scene in this checkout."""
import argparse
import csv
import json
import math
import os
import subprocess
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/MiniShotGame'
UNITY=Path('D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe')
env=os.environ.copy()
env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))

def run(command,cwd=ROOT):
    subprocess.run([str(v) for v in command],cwd=cwd,env=env,check=True)

def verify_tracking(folder,started):
    for name in ['complete.txt','frames.csv']:
        file=folder/name
        assert file.is_file() and file.stat().st_mtime>=started-.1, 'Missing/stale tracking evidence: '+name
    summary=dict(part.strip().split('=',1) for part in (folder/'complete.txt').read_text().strip().split(';'))
    assert int(summary['accepted'])>0 and summary['state']=='TRACKING', 'Tracking did not produce valid samples'
    with (folder/'frames.csv').open(newline='') as stream:rows=list(csv.DictReader(stream))
    fresh=[row for row in rows if row['fresh']=='True']
    assert fresh, 'No fresh eye sample reached a rendered frame'
    errors=[float(row['cornerErrorPx']) for row in rows]
    assert errors and all(math.isfinite(v) and v<.01 for v in errors), 'Tracking fixed-window projection failed'
    xs=[float(row['eyeX']) for row in fresh]
    assert all(math.isfinite(v) for v in xs) and max(xs)-min(xs)>.001, 'Synthetic eye motion was not rendered'
    print('TRACKING_VERIFIED accepted='+summary['accepted']+' freshFrames='+str(len(fresh)))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['export','build','checks','stills','scaled-stills','video','encode','tracking','open','aliasing-before','aliasing-after','letterbox'])
    action=parser.parse_args().action
    OUT.mkdir(parents=True,exist_ok=True)
    if action=='export':
        run(['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',ROOT/'tools/minishot/export_scene.py'])
        run(['python',ROOT/'tools/minishot/check_export.py'])
    elif action in ['build','checks']:
        run([UNITY,'-batchmode','-quit','-projectPath',ROOT/'Prototype','-executeMethod','MiniShotBuilder.Build' if action=='build' else 'MiniShotChecks.Validate','-logFile',OUT/(action+'.log')])
    elif action=='open':
        subprocess.Popen([str(UNITY),'-projectPath',str(ROOT/'Prototype'),'-executeMethod','MiniShotBuilder.Open','-logFile',str(OUT/'editor.log')],cwd=ROOT,env=env)
    elif action=='encode':
        run(['ffmpeg','-y','-framerate','30','-i',OUT/'video/%04d.png','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',OUT/'parallax.mp4'])
    else:
        dest=OUT/action;dest.mkdir(parents=True,exist_ok=True)
        command=[ROOT/'Prototype/Builds/MiniShotGame/MiniShotGame.exe','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',dest/'player.log']
        if action=='letterbox':
            command[command.index('1600')]='1200'
            command[command.index('900')]='1000'
        if action=='tracking':
            common=Path(subprocess.check_output(['git','-C',str(ROOT),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
            python=common.parent/'tracker/.venv/Scripts/python.exe'
            command+=['--tracking','--tracking-mode','simulate','--tracking-root',ROOT/'tracker','--tracking-python',python,'--tracking-output',dest,'--tracking-seconds','12','--tracking-frames']
        else:
            command+=['--minishot-capture',dest]
            if action=='video':command+=['--minishot-video']
            if action.startswith('aliasing-'):command+=['--minishot-shimmer']
            if action=='aliasing-before':command+=['--minishot-legacy-grid']
            if action=='scaled-stills':
                profile=dest/'display-profile.json'
                profile.write_text(json.dumps({'widthMetres':.636,'heightMetres':.35775,'referenceEyeMetres':{'x':0,'y':0,'z':-.6},'measured':False}),encoding='utf-8')
                command+=['--display-profile',profile]
        started=time.time()
        run(command)
        if action=='tracking':verify_tracking(dest,started)

if __name__=='__main__':main()

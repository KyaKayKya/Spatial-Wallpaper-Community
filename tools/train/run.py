"""Train export/build/capture/benchmark. Outputs stay in this worktree."""
import argparse,os,subprocess,sys,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['export','build','checks','stills','scaled-stills','video','hold','bench-dynamic','bench-static','encode'])
a=p.parse_args().action
env=os.environ.copy();env['ALLUSERSPROFILE']=env.get('ProgramData','C:/ProgramData')
out=ROOT/'Validation/Train';out.mkdir(parents=True,exist_ok=True)
def run(cmd):subprocess.run([str(x) for x in cmd],cwd=ROOT,env=env,check=True)
if a=='export':
 run(['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',ROOT/'tools/train/export_scene.py'])
 run(['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',ROOT/'tools/train/extract_panorama.py'])
elif a in ['build','checks']:run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',ROOT/'Prototype','-executeMethod','TrainBuilder.Build' if a=='build' else 'TrainBuilder.Inspect','-logFile',out/(a+'.log')])
elif a=='encode':
 for sub,name in [('video','parallax'),('hold','static-eye-journey')]:run(['ffmpeg','-y','-framerate','30','-i',out/sub/'%04d.png','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',out/(name+'.mp4')])
else:
 dest=out/a;dest.mkdir(parents=True,exist_ok=True)
 cmd=[ROOT/'Prototype/Builds/Train/Train.exe','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',dest/'player.log']
 if a.startswith('bench'):
  cmd+=['--benchmark','--output',dest]
  if a=='bench-static':cmd+=['--train-static']
 else:
  cmd+=['--train-capture',dest]
  if a=='scaled-stills':
   profile=dest/'display-profile.json';profile.write_text(json.dumps({'widthMetres':.636,'heightMetres':.35775,'referenceEyeMetres':{'x':0,'y':0,'z':-.6},'measured':False}));cmd+=['--display-profile',profile]
  if a in ('video','hold'):cmd+=['--train-video']
  if a=='hold':cmd+=['--train-hold']
 run(cmd)

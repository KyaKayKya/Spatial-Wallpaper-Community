"""Reproducible Parking export, build, framebuffer capture and standalone benchmark."""
import argparse, os, subprocess, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['export','build','rebuild','bake','bake-checks','checks','runtime','tracking','stills','stills-no-bake','box-stills','video','hold','bench-dynamic','bench-no-bake','bench-static','bench-static-always','encode'])
p.add_argument('--unity',default='D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe')
p.add_argument('--blender',default='D:/Blender/blender.exe')
p.add_argument('--ffmpeg',default='ffmpeg')
args=p.parse_args();action=args.action
env=os.environ.copy();env['ALLUSERSPROFILE']=env.get('ProgramData','C:/ProgramData')
evidence=ROOT/'Validation/Parking';evidence.mkdir(parents=True,exist_ok=True)
def run(cmd):
    result=subprocess.run([str(v) for v in cmd],cwd=ROOT,env=env)
    if result.returncode:raise SystemExit(result.returncode)
if action=='export':
    run([args.blender,'-b','--python-exit-code','1','--python',ROOT/'tools/parking/export_scene.py'])
    run([sys.executable,ROOT/'tools/parking/pack_maps.py'])
    run([sys.executable,ROOT/'tools/parking/fetch_concrete.py'])
    run([sys.executable,ROOT/'tools/parking/test_export.py'])
elif action=='bake':
    from bakery_recovery import wait_for_bake,recover
    recover()
    process=subprocess.Popen([args.unity,'-batchmode','-projectPath',str(ROOT/'Prototype'),'-executeMethod','ParkingBakeryBake.Run','-logFile',str(evidence/'bake.log')],cwd=ROOT,env=env)
    raise SystemExit(wait_for_bake(process))
elif action=='bake-checks':
    run([args.unity,'-batchmode','-quit','-projectPath',ROOT/'Prototype','-executeMethod','ParkingBakeryBake.CheckRecovery','-logFile',evidence/'bake-checks.log'])
elif action in ('build','rebuild','checks'):
    method={'build':'ParkingBuilder.Build','rebuild':'ParkingBuilder.BuildPlayerOnly','checks':'ParkingChecks.RunBatch'}[action]
    run([args.unity,'-batchmode','-quit','-projectPath',ROOT/'Prototype','-executeMethod',method,'-logFile',evidence/(action+'.log')])
elif action=='encode':
    for name,title in [('video','parallax'),('hold','static-eye-effects')]:
        run([args.ffmpeg,'-y','-framerate','30','-i',evidence/name/'%04d.png','-c:v','libx264','-preset','slow','-crf','17','-pix_fmt','yuv420p','-movflags','+faststart',evidence/(title+'.mp4')])
else:
    out=evidence/action;out.mkdir(parents=True,exist_ok=True)
    cmd=[ROOT/'Prototype/Builds/Parking/Parking.exe','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',out/'player.log']
    if action in ('stills-no-bake','bench-no-bake'):cmd+=['--parking-no-baked-lighting']
    if action=='runtime':cmd+=['--parking-checks','--output',out]
    elif action=='tracking':
        common=Path(subprocess.check_output(['git','rev-parse','--git-common-dir'],cwd=ROOT,text=True).strip())
        if not common.is_absolute():common=ROOT/common
        python=common.resolve().parent/'tracker/.venv/Scripts/python.exe'
        cmd+=['--tracking','--tracking-mode','simulate','--tracking-python',python,'--tracking-root',ROOT/'tracker','--tracking-output',out,'--tracking-seconds','12']
    elif action.startswith('bench'):
        cmd+=['--benchmark','--output',out]
        if action.startswith('bench-static'):cmd+=['--parking-static']
        if action=='bench-static-always':cmd+=['--always-render']
    else:
        cmd+=['--parking-capture',out]
        if action in ('video','hold'):cmd+=['--parking-video']
        if action=='hold':cmd+=['--parking-hold']
        if action=='box-stills':cmd+=['--parking-box-guide']
    run(cmd)

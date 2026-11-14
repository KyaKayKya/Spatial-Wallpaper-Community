"""Run RainyCafe export, Player builds, captures and matched performance tests."""
import argparse, os, subprocess, hashlib, json, datetime
from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['export','build','checks','stills','stills-wide','stills-tall','stills-no-shadows','stills-legacy-rain','video','hold','bench-full','bench-static','bench-no-shadows','encode'])
a=p.parse_args().action
out=root/'Validation/RainyCafe';out.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();env['ALLUSERSPROFILE']=env.get('ProgramData','C:/ProgramData')
if a=='export':
    cmd=['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',root/'tools/rainycafe/export_scene.py']
elif a in ('build','checks'):
    cmd=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',root/'Prototype','-executeMethod','RainyCafeBuilder.Build' if a=='build' else 'RainyCafeBuilder.Check','-logFile',out/(a+'.log')]
elif a=='encode':
    for sub in ('video','hold'):
        subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-framerate','30','-i',str(out/sub/'%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(out/(sub+'.mp4'))],check=True)
        probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-select_streams','v:0','-show_entries','stream=codec_name,width,height,avg_frame_rate,nb_frames,duration','-of','json',str(out/(sub+'.mp4'))],text=True))
        stream=probe['streams'][0];assert stream['avg_frame_rate']=='30/1' and int(stream['nb_frames'])==(1350 if sub=='hold' else 900),probe
        (out/(sub+'-video.json')).write_text(json.dumps(probe,indent=2))
    raise SystemExit()
else:
    dest=out/a;dest.mkdir(parents=True,exist_ok=True)
    width,height=(1600,686) if a=='stills-wide' else (900,1000) if a=='stills-tall' else (1600,900)
    cmd=[root/'Prototype/Builds/RainyCafe/RainyCafe.exe','-screen-fullscreen','0','-screen-width',str(width),'-screen-height',str(height),'-logFile',dest/'player.log']
    if a.startswith('bench'):
        cmd+=['--benchmark','--output',dest]
        if a=='bench-static':cmd+=['--cafe-static','--always-render']
        if a=='bench-no-shadows':cmd+=['--cafe-no-shadows']
    else:
        cmd+=['--cafe-capture',dest]
        if a=='stills-no-shadows':cmd+=['--cafe-no-shadows']
        if a=='stills-legacy-rain':cmd+=['--cafe-legacy-rain']
        if a in ('video','hold'):cmd+=['--cafe-video']
        if a=='hold':cmd+=['--cafe-hold']
with (out/(a+'-process.log')).open('w',encoding='utf-8') as log:
    if a.startswith('bench'):
        with (dest/'gpu-state.csv').open('w',encoding='utf-8') as gpu_log:
            monitor=subprocess.Popen(['nvidia-smi','--query-gpu=timestamp,name,pstate,power.draw,clocks.gr,clocks.mem,utilization.gpu,memory.used','--format=csv','-lms','1000'],stdout=gpu_log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                result=subprocess.run([str(x) for x in cmd],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
            finally:
                monitor.terminate();monitor.wait()
    else:
        result=subprocess.run([str(x) for x in cmd],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
        if a=='export' and result.returncode==0:
            result=subprocess.run(['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',str(root/'tools/rainycafe/refine_materials.py')],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
print(a,'exit',result.returncode)
if a=='build' and result.returncode==0:
    files=[root/'Prototype/Builds/RainyCafe/RainyCafe.exe',root/'Prototype/Builds/RainyCafe/RainyCafe_Data/Managed/Assembly-CSharp.dll']
    files += [f for f in (root/'Prototype/Assets/RainyCafe').rglob('*') if f.suffix in ('.cs','.shader','.unity','.json')]
    evidence={'built_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sha256':{f.relative_to(root).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in files if f.is_file()}}
    (out/'build-evidence.json').write_text(json.dumps(evidence,indent=2))
raise SystemExit(result.returncode)

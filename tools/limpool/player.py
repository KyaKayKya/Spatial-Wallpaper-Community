from pathlib import Path
import subprocess,sys,shutil,json,hashlib
r=Path(__file__).resolve().parents[2];mode=sys.argv[1];quality=sys.argv[2] if len(sys.argv)>2 else 'default'
out=r/'Validation/Limpool'/(mode+'-'+quality);out.mkdir(parents=True,exist_ok=True)
exe=r/'Prototype/Builds/Limpool/Limpool.exe'
cmd=[str(exe),'-force-d3d11','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(out/'player.log'),'--limpool-quality',quality]
if mode=='bench':cmd+=['--benchmark','--always-render','--output',str(out)]
else:
    cmd+=['--limpool-capture',str(out)]
    if mode in ['video','hold']:cmd+=['--video']
    if mode=='hold':cmd+=['--hold']
cmd+=sys.argv[3:]
def file_sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(4*1024*1024),b''):h.update(block)
    return h.hexdigest()
build={str(p.relative_to(exe.parent)):file_sha(p) for p in exe.parent.rglob('*') if p.is_file()}
fingerprint=hashlib.sha256(json.dumps(build,sort_keys=True).encode()).hexdigest()
(out/'run.json').write_text(json.dumps({'command':cmd,'exe_sha256':file_sha(exe),'build_fingerprint':fingerprint},indent=2))
p=subprocess.run(cmd,timeout=1200);print(mode,quality,'exit',p.returncode)
if p.returncode:sys.exit(p.returncode)
if mode in ['video','hold']:
    subprocess.run([shutil.which('ffmpeg'),'-y','-framerate','30','-i',str(out/'%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p',str(out.parent/('limpool-'+mode+'-'+quality+'.mp4'))],check=True,stdout=subprocess.DEVNULL,stderr=(out/'encode.log').open('w'))

"""Observe sound from the real MiniShot LPAC preview's trusted Host, no input."""
from pathlib import Path
import argparse,json,subprocess,sys,time,uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--runtime',type=Path,required=True);p.add_argument('--sdk-tgz',type=Path,required=True);args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/G/sound'/('preview-'+uuid.uuid4().hex);out.mkdir(parents=True)
compile_probe(Path(__file__).with_name('process_sound_capture.cpp'),out/'Capture.exe')
report={'status':'failed','scope':'installed SDK, actual LPAC MiniShot preview and process-specific sound; no input or RC'}
child=subprocess.Popen([sys.executable,str(ROOT/'tools/minishot/sdk.py'),'preview','--runtime',str(args.runtime.resolve()),'--sdk-tgz',str(args.sdk_tgz.resolve())],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
try:
    first=child.stdout.readline().strip();preview=Path(first);report['preview']=first;print(first,flush=True)
    deadline=time.monotonic()+180
    while not (preview/'ready.json').is_file():
        if child.poll() is not None:raise RuntimeError('Preview ended before ready: '+child.stdout.read())
        if time.monotonic()>deadline:raise TimeoutError('preview-ready')
        time.sleep(.05)
    ready=json.loads((preview/'ready.json').read_text());status=json.loads((Path(ready['workspace'])/'status.json').read_text());pid=status['host_pid'];report['host']=status
    capture=subprocess.run([str(out/'Capture.exe'),str(pid),'1500'],capture_output=True,text=True,timeout=10);(out/'capture.log').write_text(capture.stdout+capture.stderr);capture.check_returncode();sound=json.loads(capture.stdout);report['sound']=sound
    rest=child.communicate(timeout=90)[0];(out/'preview-tool.log').write_text(first+'\n'+rest);assert child.returncode==0
    assert (preview/'result.json').is_file();report['result']=json.loads((preview/'result.json').read_text());assert max(b['rms'] for b in sound['bins'])>.0001,'No actual preview sound'
    report['status']='passed-installed-LPAC-MiniShot-preview-sound-not-RC'
finally:
    if child.poll() is None:child.kill();child.wait()
    (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

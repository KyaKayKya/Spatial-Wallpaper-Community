"""Two quiet synthetic sources; verify targeted capture and target-exit silence."""
from pathlib import Path
import subprocess,uuid,json,hashlib,platform,time,sys
from native_build import compile_probe
root=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(root/'tools/sdk'))
from timed_output import ready_line
out=root/'Validation/Content/sdk-runtime/E/audio-capture'/uuid.uuid4().hex
out.mkdir(parents=True);source=Path(__file__).with_name('process_audio_capture.cpp');exe=out/'probe.exe'
compile_probe(source,exe);children=[];report={'platform':platform.platform(),'status':'not-complete'}
negative='--negative-ready' in sys.argv[1:]
try:
 for frequency in ('440','880'):
  silent=negative and frequency=='880'
  command=[sys.executable,'-c','import time;time.sleep(30)'] if silent else [str(exe),'--tone',frequency]
  process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
  children.append(process)
  ready=json.loads(ready_line(process,0.1 if silent else 5));assert ready['ready'],ready
 target,other=children
 capture=subprocess.Popen([str(exe),'--capture',str(target.pid)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(capture)
 ready=json.loads(ready_line(capture,7));report['activation']=ready
 assert ready['captureReady'],ready
 time.sleep(1.1);target.terminate();target.wait(timeout=3)
 stdout,stderr=capture.communicate(timeout=8);report['capture']=json.loads(stdout);report['stderr']=stderr
 assert capture.returncode==0,(capture.returncode,stderr)
 slices=report['capture']['slices'];before=slices[1:4];after=slices[7:]
 assert all(s['target440']>0.001 for s in before),before
 assert max(s['other880'] for s in slices)<0.0002,'unrelated-source-leaked'
 assert all(s['target440']<0.0002 for s in after),'target-exit-not-silent'
 assert report['capture']['targetExited'] and other.poll() is None,'source-lifecycle-invalid'
 features=report['capture']['features']
 assert 10<=len(features)<=15,'feature-rate-or-progress'
 assert any(p['bands'][1]>0 for p in features if p['atMs']<1000),'real-features-missing'
 assert all(all(0<=b<=15 for b in p['bands']) for p in features),'feature-precision'
 assert all(p['bands']==[0,0,0,0] for p in features if p['atMs']>1500),'ended-source-feature-retained'
 report['status']='partial-real-process-audio-isolation-only'
except TimeoutError:
 if not negative:raise
 report['status']='pass-silent-child-deadline-negative'
finally:
 for process in children:
  if process.poll() is None:process.terminate()
  process.wait(timeout=3)
 report['childrenExited']=all(p.poll() is not None for p in children)
 paths=(source,source.with_name('process_audio_support.cpp'),root/'Supervisor/Interactive/AudioFeatures.h',root/'tools/sdk/timed_output.py',Path(__file__),exe)
 report['sha256']={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out);print(json.dumps(report))

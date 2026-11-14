"""Measure the actual shared Windows runtime serially; no synthetic performance grade."""
from pathlib import Path
import sys,subprocess,argparse,json,uuid,platform,os,winreg,ctypes,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--repetitions',type=int,default=3);args=parser.parse_args()
 if not 1<=args.repetitions<=5:raise ValueError('bounded repetitions')
 out=ROOT/'Validation/Content/sdk-runtime/D/budget'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();runs=[]
 for width,height in [(640,360),(1280,720),(1920,1080)]:
  for repeat in range(args.repetitions):
   result=subprocess.run([sys.executable,str(ROOT/'Supervisor/Tests/run_production.py'),'--build',str(args.worker),'--width',str(width),'--height',str(height),'--steady-ms','5000'],check=True,capture_output=True,text=True,timeout=120)
   path=Path(result.stdout.strip().splitlines()[-1]);report=json.loads((path/'report.json').read_text());r=report['results'];assert r['actual_gpu_budget_trip'] and 0<=r['gpu_budget_kill_to_kernel_exit_ms']<=500 and r['steady_frames']>0 and r['runtime_copies_reclaimed']
   runs.append({'run':path.relative_to(ROOT).as_posix(),'report_sha256':hashlib.sha256((path/'report.json').read_bytes()).hexdigest(),'repeat':repeat,**{k:v for k,v in r.items() if not k.endswith('_samples')}})
   print(width,height,repeat,'private',r['healthy_peak_private_bytes'],'GPU',r['actual_gpu_peak_bytes'],'stop',r['normal_stop_completion_ms'],flush=True)
 assert sources()==receipt,'sources changed during measurement'
 with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as key:cpu=winreg.QueryValueEx(key,'ProcessorNameString')[0]
 summary={'status':'reference-host-runtime-measurements-not-performance-grade','platform':platform.platform(),'cpu':cpu.strip(),'logical_processors':os.cpu_count(),'worker_build':str(args.worker),'sources':receipt,'runs':runs,'max_first_frame_ms':max(r['first_durable_frame_ms'] for r in runs),'max_normal_stop_completion_ms':max(r['normal_stop_completion_ms'] for r in runs),'max_gpu_kill_to_kernel_exit_ms':max(r['gpu_budget_kill_to_kernel_exit_ms'] for r in runs),'max_private_bytes':max(r['healthy_peak_private_bytes'] for r in runs),'max_actual_gpu_bytes':max(r['actual_gpu_peak_bytes'] for r in runs),'max_frame_gap_ms':max(r['max_frame_gap_ms'] for r in runs)}
 (out/'report.json').write_text(json.dumps(summary,indent=2));print(out)
if __name__=='__main__':main()

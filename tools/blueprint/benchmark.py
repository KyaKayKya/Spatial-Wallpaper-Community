"""Measure actual Players sequentially; includes OS process and GPU memory counters."""
from pathlib import Path
import argparse,ctypes as c,json,os,subprocess,time,psutil
root=Path(__file__).resolve().parents[2]
class ValueUnion(c.Union):_fields_=[('large',c.c_longlong),('double',c.c_double),('long',c.c_long)]
class Value(c.Structure):_fields_=[('status',c.c_ulong),('value',ValueUnion)]
class Item(c.Structure):_fields_=[('name',c.c_wchar_p),('value',Value)]
class GpuMemory:
 def __init__(self):
  self.query=c.c_void_p();self.counter=c.c_void_p();self.error=None;self.pdh=c.WinDLL('pdh')
  self.pdh.PdhOpenQueryW.argtypes=[c.c_wchar_p,c.c_size_t,c.POINTER(c.c_void_p)]
  self.pdh.PdhAddEnglishCounterW.argtypes=[c.c_void_p,c.c_wchar_p,c.c_size_t,c.POINTER(c.c_void_p)]
  self.pdh.PdhCollectQueryData.argtypes=[c.c_void_p]
  self.pdh.PdhGetFormattedCounterArrayW.argtypes=[c.c_void_p,c.c_ulong,c.POINTER(c.c_ulong),c.POINTER(c.c_ulong),c.c_void_p]
  self.pdh.PdhCloseQuery.argtypes=[c.c_void_p]
  for result in [self.pdh.PdhOpenQueryW(None,0,c.byref(self.query)),self.pdh.PdhAddEnglishCounterW(self.query,r'\GPU Process Memory(*)\Dedicated Usage',0,c.byref(self.counter))]:
   if result:self.error=hex(result&0xffffffff)
 def sample(self,pid):
  if self.error:return None
  self.pdh.PdhCollectQueryData(self.query);size=c.c_ulong();count=c.c_ulong();self.pdh.PdhGetFormattedCounterArrayW(self.counter,0x400,c.byref(size),c.byref(count),None)
  if not size.value:return None
  buffer=c.create_string_buffer(size.value);result=self.pdh.PdhGetFormattedCounterArrayW(self.counter,0x400,c.byref(size),c.byref(count),buffer)
  if result:return None
  values=c.cast(buffer,c.POINTER(Item));matched=[values[i].value.value.large for i in range(count.value) if values[i].name.startswith('pid_'+str(pid)+'_') and values[i].value.status in (0,1)]
  return sum(matched) if matched else None
 def close(self):
  if self.query:self.pdh.PdhCloseQuery(self.query)
def run(source,quality,look,label,terrain='none',hour=16):
 output=root/'Validation/Blueprint/performance'/label;output.mkdir(parents=True,exist_ok=True)
 command=[str(root/'Prototype/Builds/Blueprint/Blueprint.exe'),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(output/'player.log'),'--blueprint',str(source),'--blueprint-data-root',str(root/'Validation/Blueprint/benchmark-data'),'--blueprint-quality',str(quality),'--blueprint-look',look,'--blueprint-hour',str(hour),'--blueprint-yaw','20','--blueprint-benchmark',str(output)]
 command+=['--blueprint-terrain',terrain]
 gpu=GpuMemory();peak=0;private=0;vram=[];start=time.monotonic();process=subprocess.Popen(command,cwd=root/'Prototype');watched=psutil.Process(process.pid)
 try:
  while process.poll() is None:
   if time.monotonic()-start>120:process.kill();raise RuntimeError('Player benchmark timed out')
   try:memory=watched.memory_info();peak=max(peak,memory.rss);private=max(private,getattr(memory,'private',0));value=gpu.sample(process.pid);vram.extend([] if value is None else [value])
   except psutil.NoSuchProcess:pass
   time.sleep(.1)
 finally:gpu.close()
 if process.returncode:raise RuntimeError('Player benchmark failed: '+str(output))
 report=json.loads((output/'performance.json').read_text(encoding='utf-8-sig'));report.update(source=source.name,source_bytes=source.stat().st_size,quality=quality,look=look,wall_elapsed_s=time.monotonic()-start,peak_working_set_mib=peak/1048576,peak_private_mib=private/1048576,peak_dedicated_gpu_mib=max(vram)/1048576 if vram else None,gpu_memory_counter='Windows PDH GPU Process Memory/Dedicated Usage',gpu_memory_counter_error=gpu.error)
 report['terrain']=terrain;report['hour']=hour
 scene=output/'import.json'
 if scene.exists():report['import']=json.loads(scene.read_text(encoding='utf-8-sig'))
 (output/'performance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(report,ensure_ascii=False),flush=True);return report
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--quick',action='store_true');a=p.parse_args();reports=[]
 sources=[root/'Validation/Blueprint/corpus'/('synthetic-'+str(n)+'.schem') for n in [10000,50000,200000]]
 for source in sources:
  for quality in ([1] if a.quick else [0,1,2]):reports.append(run(source,quality,'warm',source.stem+'-q'+str(quality)))
 if not a.quick:
  for look in ['clear','natural']:reports.append(run(sources[1],1,look,'synthetic-50000-'+look))
 destination=root/'Validation/Blueprint/performance/summary.json';destination.write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf8')

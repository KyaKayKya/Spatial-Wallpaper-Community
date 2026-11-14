using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using System;
using System.IO;
using System.Globalization;

[DefaultExecutionOrder(50)]
public sealed class ShowcaseBenchmark:MonoBehaviour {
 public WindowProjection projection;
 public AdaptiveRenderCadence cadence;
 public float demoEyeVerticalScale=1;
 bool active;float start,previousCpuTime;int phase=-1,renderedCount;double previousCpu,cpuPercent;
 ulong lastTimestamp;
 readonly FrameTiming[] timings=new FrameTiming[1];
 StreamWriter csv;
 void Start(){
  var args=Environment.GetCommandLineArgs();active=Array.IndexOf(args,"--benchmark")>=0;
  if(!active)return;
  cadence.adaptive=Array.IndexOf(args,"--always-render")<0;
  GetComponent<PreviewEyeDriver>().allowInput=false;
  int ix=Array.IndexOf(args,"--output");
  string folder=ix>=0&&ix+1<args.Length?args[ix+1]:Application.persistentDataPath;
  Directory.CreateDirectory(folder);
  File.WriteAllText(Path.Combine(folder,"hardware.txt"),"Unity="+Application.unityVersion+"\nGPU="+SystemInfo.graphicsDeviceName+
   "\nAPI="+SystemInfo.graphicsDeviceType+"\nCPU="+SystemInfo.processorType+"\nRAM_MB="+SystemInfo.systemMemorySize+
   "\nresolution="+Screen.width+"x"+Screen.height+"\nrefresh="+Screen.currentResolution.refreshRateRatio.value+
   "\nEditor="+Application.isEditor+"\nadaptive="+cadence.adaptive+
   "\nCapture=FrameTimingManager, delayed samples; deduplicate frame_timestamp and exclude phase transitions; no webcam tracker\n");
  csv=new StreamWriter(Path.Combine(folder,"frames.csv"));
  csv.WriteLine("seconds,phase,render_interval,rendered_count,update_ms,frame_timestamp,new_timing,cpu_frame_ms,cpu_main_ms,present_wait_ms,cpu_render_ms,gpu_ms,working_set_bytes,private_bytes,unity_allocated_bytes,unity_reserved_bytes,managed_bytes,process_cpu_percent");
  NativeProcessCounters.Sample();start=Time.realtimeSinceStartup;previousCpuTime=start;previousCpu=NativeProcessCounters.CpuSeconds;
 }
 void LateUpdate(){
  if(!active)return;
  float elapsed=Time.realtimeSinceStartup-start;int next=elapsed<10?0:elapsed<70?1:elapsed<130?2:3;
  if(next==3){csv.Dispose();csv=null;active=false;Application.Quit();return;}
  if(next!=phase){csv.Flush();phase=next;}
  var eye=phase==2?WindowProjection.DemoEye(elapsed-70):projection.referenceEye;
  if(phase==2)eye.y*=demoEyeVerticalScale;
  projection.SetEye(eye);
  if(Time.realtimeSinceStartup-previousCpuTime>=1){
   NativeProcessCounters.Sample();double now=NativeProcessCounters.CpuSeconds;
   cpuPercent=100*(now-previousCpu)/((Time.realtimeSinceStartup-previousCpuTime)*Environment.ProcessorCount);
   previousCpu=now;previousCpuTime=Time.realtimeSinceStartup;
  }
  FrameTimingManager.CaptureFrameTimings();uint n=FrameTimingManager.GetLatestTimings(1,timings);var f=timings[0];
  bool fresh=n>0&&f.frameStartTimestamp!=lastTimestamp; if(n>0)lastTimestamp=f.frameStartTimestamp;
  if(OnDemandRendering.willCurrentFrameRender)renderedCount++;
  if(!fresh&&!OnDemandRendering.willCurrentFrameRender)return;
  object[] values={elapsed,phase,OnDemandRendering.renderFrameInterval,renderedCount,Time.unscaledDeltaTime*1000,
   n>0?f.frameStartTimestamp:0,fresh?1:0,n>0?f.cpuFrameTime:double.NaN,n>0?f.cpuMainThreadFrameTime:double.NaN,
   n>0?f.cpuMainThreadPresentWaitTime:double.NaN,n>0?f.cpuRenderThreadFrameTime:double.NaN,
   n>0&&f.gpuFrameTime>0?f.gpuFrameTime:double.NaN,NativeProcessCounters.WorkingSet,NativeProcessCounters.PrivateBytes,Profiler.GetTotalAllocatedMemoryLong(),
   Profiler.GetTotalReservedMemoryLong(),GC.GetTotalMemory(false),cpuPercent};
  csv.WriteLine(string.Join(",",Array.ConvertAll(values,x=>Convert.ToString(x,CultureInfo.InvariantCulture))));
 }
 void OnDestroy(){if(csv!=null)csv.Dispose();}
}

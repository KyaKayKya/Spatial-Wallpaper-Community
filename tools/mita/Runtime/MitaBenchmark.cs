using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaBenchmark : MonoBehaviour {
  [Serializable] sealed class Stage {
   public string name;public int frames;public double seconds,fps,mean_cpu_ms,mean_gpu_ms,p95_frame_ms,cpu_percent_one_core;public long working_set_bytes,private_bytes;
  }
  [Serializable] sealed class Report {public string unity,gpu,os;public int width,height;public List<Stage> stages=new List<Stage>();}
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--benchmark")<0)yield break;
   var projection=GetComponent<MitaBehaviour>().projection;
   var report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,os=SystemInfo.operatingSystem,width=Screen.width,height=Screen.height};
   yield return new WaitForSecondsRealtime(10);
   foreach(bool moving in new[]{false,true}){
    var stage=new Stage{name=moving?"moving":"static"};var durations=new List<double>();var timing=new FrameTiming[1];int cpuSamples=0,gpuSamples=0;
    NativeProcessCounters.Sample();double cpuStart=NativeProcessCounters.CpuSeconds;float start=Time.realtimeSinceStartup;
    while(Time.realtimeSinceStartup-start<30){
     if(moving)projection.SetEye(WindowProjection.DemoEye(Time.realtimeSinceStartup-start));
     FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
     durations.Add(Time.unscaledDeltaTime*1000);stage.frames++;
     if(FrameTimingManager.GetLatestTimings(1,timing)>0){
      if(timing[0].cpuFrameTime>0){stage.mean_cpu_ms+=timing[0].cpuFrameTime;cpuSamples++;}
      if(timing[0].gpuFrameTime>0){stage.mean_gpu_ms+=timing[0].gpuFrameTime;gpuSamples++;}
     }
    }
    stage.seconds=Time.realtimeSinceStartup-start;stage.fps=stage.frames/stage.seconds;
    stage.mean_cpu_ms=cpuSamples==0?-1:stage.mean_cpu_ms/cpuSamples;stage.mean_gpu_ms=gpuSamples==0?-1:stage.mean_gpu_ms/gpuSamples;
    durations.Sort();stage.p95_frame_ms=durations[(int)((durations.Count-1)*.95)];
    NativeProcessCounters.Sample();stage.working_set_bytes=NativeProcessCounters.WorkingSet;stage.private_bytes=NativeProcessCounters.PrivateBytes;
    stage.cpu_percent_one_core=(NativeProcessCounters.CpuSeconds-cpuStart)/stage.seconds*100;
    report.stages.Add(stage);
   }
   File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("MITA_OUTPUT"),"performance.json"),JsonUtility.ToJson(report,true));Application.Quit();
  }
 }
}

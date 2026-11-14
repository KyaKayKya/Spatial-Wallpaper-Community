using System;using System.Diagnostics;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted diagnostics only; no author binding and no process CPU substitute.
 // Unity reports main/render work separately from Present/target-FPS wait.
 internal sealed class WorkerFrameTiming {
  readonly FrameTiming[] cache=new FrameTiming[16];readonly JObject header;readonly Stopwatch clock=Stopwatch.StartNew();JArray rows=new JArray();ulong lastFrame;long sequence,lastFlush;bool unavailable;
  internal WorkerFrameTiming(WorkerIdentity identity,string build,int width,int height,int fps){header=new JObject{{"format",1},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation},{"build",build},{"width",width},{"height",height},{"target_fps",fps},{"cpu_timer_frequency",FrameTimingManager.GetCpuTimerFrequency()}};}
  internal void Poll(){if(unavailable)return;try{
   FrameTimingManager.CaptureFrameTimings();uint count=FrameTimingManager.GetLatestTimings((uint)cache.Length,cache);
   for(int i=(int)count-1;i>=0;i--){var frame=cache[i];if(frame.frameStartTimestamp==0||frame.frameStartTimestamp<=lastFrame)continue;lastFrame=frame.frameStartTimestamp;
    rows.Add(new JArray(++sequence,frame.frameStartTimestamp,frame.cpuFrameTime,frame.cpuMainThreadFrameTime,frame.cpuRenderThreadFrameTime,frame.cpuMainThreadPresentWaitTime,frame.gpuFrameTime));if(rows.Count==32)Flush();
   }
   if(clock.ElapsedMilliseconds-lastFlush>=500)Flush();
  }catch(Exception error){Unavailable(error);}}
  void Unavailable(Exception error){if(unavailable)return;unavailable=true;UnityEngine.Debug.Log("SDK_FRAME_TIMING_UNAVAILABLE "+error.GetType().Name);}
  internal void Flush(){if(unavailable||rows.Count==0)return;try{var record=(JObject)header.DeepClone();record["rows"]=rows;UnityEngine.Debug.Log("SDK_FRAME_TIMINGS "+record.ToString(Formatting.None));rows=new JArray();lastFlush=clock.ElapsedMilliseconds;}catch(Exception error){Unavailable(error);}}
 }
}

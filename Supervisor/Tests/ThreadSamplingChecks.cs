using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Threading;using Newtonsoft.Json.Linq;
static class ThreadSamplingChecks {
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 static bool Seen(JObject value,int id,string state,string reason=null){foreach(JObject sample in (JArray)value["samples"])foreach(JObject thread in (JArray)sample["threads"])if((int)thread["id"]==id&&(string)thread["state"]==state&&(reason==null||(string)thread["wait"]==reason))return true;return false;}
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(string[] args){if(args.Length>0&&args[0]=="--child"){Console.WriteLine(GetCurrentThreadId());var time=Stopwatch.StartNew();while(time.ElapsedMilliseconds<20000)Thread.SpinWait(1000);return 0;}
  try{Need(Console.ReadLine()=="go","outer-job-assigned-before-child");string output=args[0];bool negative=Array.IndexOf(args,"--negative")>=0;using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--child"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})){
   OwnedHelperPause pause=null;OwnedThreadSampler sampler=null;
   try{int tid=int.Parse(child.StandardOutput.ReadLine());Need(child.Handle!=IntPtr.Zero,"held-child-handle");sampler=new OwnedThreadSampler(child,negative);Thread.Sleep(200);var before=sampler.Snapshot();File.WriteAllText(Path.Combine(output,"before.json"),before.ToString());Need(Seen(before,tid,"Running")||Seen(before,tid,"Ready"),"busy-child-observed-runnable");
    pause=new OwnedHelperPause(child);Need(!pause.Exited,"paused-child-still-live");File.WriteAllText(Path.Combine(output,"paused-child"),child.Id.ToString());if(Array.IndexOf(args,"--wait-owner-kill")>=0)Thread.Sleep(20000);Thread.Sleep(200);var after=sampler.Snapshot();File.WriteAllText(Path.Combine(output,"after.json"),after.ToString());Need(Seen(after,tid,"Wait","Suspended"),"paused-child-observed-suspended");Need((string)after["error"]==null,"sampler-no-errors");
   }finally{try{sampler?.Dispose();}finally{try{pause?.Dispose();}finally{if(!child.HasExited)child.Kill();Need(child.WaitForExit(3000),"owned-child-exited");}}}
  }return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

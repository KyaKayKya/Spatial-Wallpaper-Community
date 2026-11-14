using System;using System.Diagnostics;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Sdk.Capabilities;
static class DesktopHitCallbackChecks {
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern uint MsgWaitForMultipleObjectsEx(uint count,IntPtr handles,uint timeout,uint mask,uint flags);
 sealed class Receiver:Form {
  readonly bool msaa;
  public long Received;public uint ReceivingThread;public int Count;public readonly uint OwnerThread=GetCurrentThreadId();
  public Receiver(bool msaa){this.msaa=msaa;var handle=Handle;}
  protected override void WndProc(ref Message message){if(message.Msg==(msaa?0x3d:0x8000+777)){Received=Stopwatch.GetTimestamp();ReceivingThread=GetCurrentThreadId();Count++;if(!msaa){message.Result=new IntPtr(0x5344);return;}}base.WndProc(ref message);}
 }
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 [STAThread]static int Main(string[] args){
  if(Console.ReadLine()!="go")return 2;
  var report=new JObject{{"scope","owned synchronous callback control, not Explorer or MSAA root cause"},{"budget_ms",25},{"frequency",Stopwatch.Frequency}};
  try{bool background=args[2]=="background",msaa=args.Length>3&&args[3]=="--msaa";report["mode"]=args[2];report["msaa"]=msaa;if(msaa)report["scope"]="owned hidden window AccessibleObjectFromWindow; not Explorer AccessibleObjectFromPoint root cause";
   using(var window=new Receiver(msaa))using(var client=new DesktopHitClient(args[0]))using(var child=Process.GetProcessById(client.ProcessId)){
    IntPtr retained=child.Handle;
    try{
     // Warm the same protocol and task dispatch without a window callback.
     Need(client.Query(0,0)!=null,"protocol-warmup");Task.Run(()=>0).GetAwaiter().GetResult();
     long hwnd=window.Handle.ToInt64();int x=unchecked((int)hwnd),y=unchecked((int)(hwnd>>32));
     long began=Stopwatch.GetTimestamp();DesktopHitResult hit;
     if(background){var query=Task.Run(()=>client.Query(x,y));var bound=Stopwatch.StartNew();while(!query.IsCompleted){Application.DoEvents();if(bound.ElapsedMilliseconds>1000)throw new TimeoutException("callback-control-outer-bound");MsgWaitForMultipleObjectsEx(0,IntPtr.Zero,1,0x4ff,4);}hit=query.GetAwaiter().GetResult();}
     else hit=client.Query(x,y);
     long ended=Stopwatch.GetTimestamp();string trace=client.FailureTrace??(string)typeof(DesktopHitClient).GetMethod("SnapshotTrace",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(client,null);
     report["query_begin_qpc"]=began;report["query_end_qpc"]=ended;report["callback_qpc"]=window.Received;report["callback_count"]=window.Count;report["owner_thread"]=window.OwnerThread;report["callback_thread"]=window.ReceivingThread;report["trace"]=trace;report["failure"]=client.Failure;report["failure_phase"]=client.FailurePhase;
     Need(trace.StartsWith("sequence=2;stages="),"same-second-query-trace");string[] slots=trace.Substring(trace.IndexOf("stages=")+7).Split(';')[0].Split(',');
     Need(long.Parse(slots[1])>=began&&long.Parse(slots[1])<=ended,"helper-entered-synchronous-callback-within-query");
     if(background){Need(hit!=null&&client.Failure==null,"background-same-25ms-query-completed");Need(window.Count>=1&&window.ReceivingThread==window.OwnerThread&&window.Received>=began&&window.Received<=ended,"callback-executed-on-owner-during-query");Need(long.Parse(slots[2])>=window.Received,"helper-observed-callback-return");if(msaa)Need(long.Parse(slots[4])==0,"actual-MSAA-object-obtained");Need(hit.IsFresh(WindowsMachineClock.Milliseconds()),"reply-original-age-still-fresh");}
     else{Need(hit==null&&client.Failure=="deadline"&&client.FailurePhase=="read","synchronous-query-deadline-rejected");Need(window.Count==0&&long.Parse(slots[2])==0,"owner-did-not-dispatch-before-query-return");}
     report["status"]="controlled-dependency-observed";
    }finally{client.Dispose();Need(client.Completion.Wait(3000),"callback-helper-cleanup-completed");Need(child.WaitForExit(1000),"callback-helper-kernel-exited");report["helperExited"]=true;}
   }return 0;
  }catch(Exception error){report["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{File.WriteAllText(args[1],report.ToString());}
 }
}

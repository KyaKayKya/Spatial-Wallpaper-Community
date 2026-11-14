using System;using System.ComponentModel;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Interactive;
static class InputThreadMsaaChecks {
 delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
 [StructLayout(LayoutKind.Sequential)]struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct MouseHook{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct Payload{[FieldOffset(0)]public Mouse Mouse;}
 [StructLayout(LayoutKind.Sequential)]struct Input{public uint Type;public Payload Data;}
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int kind,Hook callback,IntPtr module,uint thread);
 [DllImport("user32.dll",SetLastError=true)]static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] data,int size);
 [DllImport("user32.dll")]static extern uint MsgWaitForMultipleObjectsEx(uint count,IntPtr handles,uint timeout,uint mask,uint flags);
 [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(Microsoft.Win32.SafeHandles.SafeWaitHandle handle,uint timeout);
 sealed class Receiver:Form{public int Calls;public uint CallbackThread;protected override void WndProc(ref Message message){if(message.Msg==0x3d){Calls++;CallbackThread=GetCurrentThreadId();}base.WndProc(ref message);}}
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]static int Main(string[] args){if(Console.ReadLine()!="go")return 2;var report=new JObject{{"scope","real low-level hook on independent owner with owned-window MSAA, not Explorer acceptance"}};
  try{bool blocked=Array.IndexOf(args,"--block-ui")>=0,ownerCallback=Array.IndexOf(args,"--callback-on-owner")>=0,asyncQuery=Array.IndexOf(args,"--async-query")>=0;report["blockedUi"]=blocked;report["callbackOnOwner"]=ownerCallback;report["asyncQuery"]=asyncQuery;uint ui=GetCurrentThreadId(),hookThread=0;IntPtr hook=IntPtr.Zero;DesktopHitClient client=null;Process helper=null;Receiver ownerWindow=null;Task querying=null;var result=new TaskCompletionSource<DesktopHitResult>(TaskCreationOptions.RunContinuationsAsynchronously);long queryStart=0,queryEnd=0;int handled=0,callbacksAtReturn=0;
   using(var window=new Receiver()){long hwnd=window.Handle.ToInt64();Task<uint> injected=null;
    Action query=()=>{try{queryStart=Stopwatch.GetTimestamp();var hit=client.Query(unchecked((int)hwnd),unchecked((int)(hwnd>>32)));queryEnd=Stopwatch.GetTimestamp();callbacksAtReturn=(ownerCallback?ownerWindow:window).Calls;result.TrySetResult(hit);}catch(Exception error){result.TrySetException(error);}};
    Hook callback=(code,message,data)=>{try{if(code==0&&message.ToInt64()==0x200&&((MouseHook)Marshal.PtrToStructure(data,typeof(MouseHook))).Extra.ToUInt64()==0x53574443&&handled++==0){hookThread=GetCurrentThreadId();if(asyncQuery)querying=Task.Run(query);else query();}}catch(Exception error){result.TrySetException(error);}return CallNextHookEx(hook,code,message,data);};
    var owner=new OwnedInputThread(()=>{if(ownerCallback){ownerWindow=new Receiver();hwnd=ownerWindow.Handle.ToInt64();}client=new DesktopHitClient(args[0]);helper=Process.GetProcessById(client.ProcessId);var retained=helper.Handle;Need(client.Query(0,0)!=null,"owner-helper-warmup");hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);if(hook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());},()=>{},()=>{if(hook!=IntPtr.Zero){if(!UnhookWindowsHookEx(hook))throw new Win32Exception(Marshal.GetLastWin32Error());hook=IntPtr.Zero;}client?.Dispose();bool complete=(client==null||client.Completion.IsCompleted)&&(querying==null||querying.IsCompleted);if(complete)ownerWindow?.Dispose();return complete;});
    try{
     var startup=Stopwatch.StartNew();while(!owner.Started.IsCompleted&&startup.ElapsedMilliseconds<3000){Application.DoEvents();MsgWaitForMultipleObjectsEx(0,IntPtr.Zero,1,0x4ff,4);}Need(owner.Started.IsCompleted,"owner-start-within-deadline");owner.Started.GetAwaiter().GetResult();
     injected=Task.Run(()=>{uint count=SendInput(2,new[]{new Input{Data=new Payload{Mouse=new Mouse{X=1,Flags=1|0x2000,Extra=new UIntPtr(0x53574443)}}},new Input{Data=new Payload{Mouse=new Mouse{X=-1,Flags=1|0x2000,Extra=new UIntPtr(0x53574443)}}}},Marshal.SizeOf(typeof(Input)));Console.WriteLine("INJECTION count="+count+" error="+Marshal.GetLastWin32Error()+" size="+Marshal.SizeOf(typeof(Input))+" pointer="+IntPtr.Size);return count;});
     if(blocked)Need(WaitForSingleObject(((IAsyncResult)result.Task).AsyncWaitHandle.SafeWaitHandle,1000)==0,"blocked-ui-query-returned");
     else{var clock=Stopwatch.StartNew();while(!result.Task.IsCompleted&&clock.ElapsedMilliseconds<1000){Application.DoEvents();MsgWaitForMultipleObjectsEx(0,IntPtr.Zero,1,0x4ff,4);}Need(result.Task.IsCompleted,"pumped-ui-query-returned");}
     var reply=result.Task.GetAwaiter().GetResult();Need(injected.Wait(1000),"mouse-injection-within-deadline");Need(injected.GetAwaiter().GetResult()==2,"actual-paired-mouse-moves-delivered");Need(hookThread!=0&&hookThread!=ui,"actual-hook-runs-off-receiver-ui");
     var receiver=ownerCallback?ownerWindow:window;report["ui_thread"]=ui;report["hook_thread"]=hookThread;report["msaa_callbacks"]=receiver.Calls;report["callbacks_at_query_return"]=callbacksAtReturn;report["callback_thread"]=receiver.CallbackThread;report["query_ms"]=(queryEnd-queryStart)*1000.0/Stopwatch.Frequency;report["failure"]=client.Failure;report["trace"]=client.FailureTrace;
     if(ownerCallback&&!asyncQuery)Need(reply==null&&client.Failure=="deadline"&&callbacksAtReturn==0,"synchronous-owner-callback-dependency-fails-closed");
     else if(ownerCallback)Need(reply!=null&&client.Failure==null&&callbacksAtReturn>0&&receiver.CallbackThread==hookThread,"async-query-owner-MSAA-callback-completes");
     else if(blocked)Need(reply==null&&client.Failure=="deadline"&&window.Calls==0,"blocked-receiver-ui-still-fails-closed");
     else Need(reply!=null&&client.Failure==null&&window.Calls>0&&window.CallbackThread==ui,"real-hook-query-and-receiver-MSAA-complete");
    }finally{owner.Dispose();var stop=Stopwatch.StartNew();while(!owner.Completion.IsCompleted&&stop.ElapsedMilliseconds<3000){Application.DoEvents();MsgWaitForMultipleObjectsEx(0,IntPtr.Zero,1,0x4ff,4);}Need(owner.Completion.IsCompleted,"owner-stop-bounded");owner.Completion.GetAwaiter().GetResult();Need(hook==IntPtr.Zero,"actual-hook-unregistered");if(helper!=null){Need(helper.WaitForExit(1000),"helper-kernel-exited");helper.Dispose();}if(injected!=null)Need(injected.Wait(1000),"injection-task-owned-through-cleanup");GC.KeepAlive(callback);}
   }report["status"]="partial-independent-hook-msaa-control";return 0;
  }catch(Exception error){report["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{File.WriteAllText(args[1],report.ToString());}
 }
}

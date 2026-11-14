using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Collections.Generic;
using Newtonsoft.Json.Linq;
// Test-only explicit-thread observations; not used by production input.
// One caller owns Capture/Snapshot/Dispose; the borrowed Process must remain live.
sealed class OwnedThreadEvidence : IDisposable {
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenThread(uint access,bool inherit,uint tid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint GetProcessIdOfThread(IntPtr thread);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetThreadTimes(IntPtr thread,out ulong creation,out ulong exit,out ulong kernel,out ulong user);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryThreadCycleTime(IntPtr thread,out ulong cycles);
 readonly Process process;readonly int pid;readonly uint tid;readonly ulong creation;readonly Func<IntPtr,uint> wait;IntPtr handle;long ordinal;bool disposed;
 readonly Queue<JObject> samples=new Queue<JObject>();const int Capacity=64;
 public OwnedThreadEvidence(Process owned,uint threadId,ulong? expectedCreation=null,Func<IntPtr,uint> waitProbe=null){
  if(owned==null||owned.Handle==IntPtr.Zero||owned.HasExited)throw new ArgumentException("owned-process-not-live");
  process=owned;pid=owned.Id;tid=threadId;wait=waitProbe??(h=>WaitForSingleObject(h,0));
  handle=OpenThread(0x100800,false,tid);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"thread-open");
  try{
   ulong exit,kernel,user;
   if(GetProcessIdOfThread(handle)!=(uint)pid||owned.HasExited)throw new InvalidOperationException("thread-process-identity-mismatch");
   if(!GetThreadTimes(handle,out creation,out exit,out kernel,out user))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"thread-creation");
   if(expectedCreation.HasValue&&expectedCreation.Value!=creation)throw new InvalidOperationException("thread-creation-identity-mismatch");
   RequireLive(null);
  }catch{Dispose();throw;}
 }
 void RequireLive(JObject row){
  uint result=wait(handle);int error=Marshal.GetLastWin32Error();if(row!=null)row["wait_result"]=result;
  if(result==258)return;
  if(result==0)throw new InvalidOperationException("target-exited");
  if(result==uint.MaxValue)throw new System.ComponentModel.Win32Exception(error,"thread-wait-failed");
  throw new InvalidOperationException("thread-wait-unknown:"+result);
 }
 public JObject Capture(){
  var row=new JObject{{"ordinal",++ordinal},{"begin_qpc",Stopwatch.GetTimestamp()},{"frequency",Stopwatch.Frequency},{"pid",pid},{"tid",tid},{"creation_filetime",creation},{"status","unavailable"},{"classification","indeterminate"}};
  try{
   if(disposed)throw new InvalidOperationException("probe-disposed");
   if(process.HasExited)throw new InvalidOperationException("target-process-exited");RequireLive(row);
   ulong current,exit,kernel,user,cycles;
   if(!GetThreadTimes(handle,out current,out exit,out kernel,out user))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"thread-times");
   if(current!=creation||GetProcessIdOfThread(handle)!=(uint)pid)throw new InvalidOperationException("thread-identity-changed");
   if(!QueryThreadCycleTime(handle,out cycles))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"thread-cycles");
   row["kernel_100ns"]=kernel;row["user_100ns"]=user;row["cycles"]=cycles;
   process.Refresh();bool found=false;var threads=process.Threads;
   try{
    if(threads.Count>64)throw new InvalidOperationException("thread-enumeration-capacity");
    foreach(ProcessThread thread in threads){if(thread.Id==(int)tid){var state=thread.ThreadState;row["state"]=state.ToString();row["wait_reason"]=state==System.Diagnostics.ThreadState.Wait?thread.WaitReason.ToString():null;found=true;}}
   }finally{foreach(ProcessThread thread in threads)thread.Dispose();}
   if(!found)throw new InvalidOperationException("target-thread-unobserved");
   if(process.HasExited)throw new InvalidOperationException("target-process-exited-during-sample");RequireLive(row);
   row["status"]="observed";
  }catch(Exception error){row["error"]=error.GetType().Name+":"+error.Message;var native=error as System.ComponentModel.Win32Exception;if(native!=null)row["win32_error"]=native.NativeErrorCode;}
  row["end_qpc"]=Stopwatch.GetTimestamp();samples.Enqueue((JObject)row.DeepClone());while(samples.Count>Capacity)samples.Dequeue();return row;
 }
 public JObject Snapshot(){return new JObject{{"capacity",Capacity},{"total",ordinal},{"samples",new JArray(samples)},{"wct",new JObject{{"status","unavailable"},{"reason","not-enabled-no-privilege-adjustment"}}},{"scope","test-only polling; independent observations, not a scheduler trace; no automatic wait-vs-scheduling attribution"}};}
 public void Dispose(){if(disposed)return;disposed=true;if(handle!=IntPtr.Zero){var saved=handle;handle=IntPtr.Zero;if(!CloseHandle(saved))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"thread-handle-close");}}
}

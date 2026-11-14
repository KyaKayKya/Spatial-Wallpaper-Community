using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SpatialWallpaper.Supervision.Windows;

// Test-only executor. Atomically create suspended inside a kill-on-close Job, then resume.
// The caller must record potential key/button releases before invoking Run.
internal static class OwnedInputInjector {
 internal delegate bool CursorRead(out int x,out int y);
 // Test-only pre-send rejection. Native checks retain their original order;
 // extra observations and file I/O happen only after rejection is irrevocable.
 internal static bool Preflight(long expected,int x,int y,string mode,string path,Func<long> foreground,CursorRead cursor,Action<string,string> save=null){
  long began=Stopwatch.GetTimestamp(),actual=foreground();int cx=0,cy=0;bool checkedCursor=false,ok=false;long cursorAt=0;
  if(actual==expected){checkedCursor=true;ok=cursor(out cx,out cy);cursorAt=Stopwatch.GetTimestamp();if(ok&&cx==x&&cy==y)return true;}
  long rejected=Stopwatch.GetTimestamp();
  try{
   long machine=SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();
   if(!checkedCursor){ok=cursor(out cx,out cy);cursorAt=Stopwatch.GetTimestamp();}
   var record=new Newtonsoft.Json.Linq.JObject{{"schema",1},{"exit_code",3},{"mode",mode=="send"||mode=="repeat-unmapped"?mode:"other"},{"expected_foreground",expected},{"actual_foreground",actual},{"expected_x",x},{"expected_y",y},{"cursor_succeeded",ok},{"cursor_phase",checkedCursor?"validation":"diagnostic-after-rejection"},{"actual_x",ok?(Newtonsoft.Json.Linq.JToken)cx:Newtonsoft.Json.Linq.JValue.CreateNull()},{"actual_y",ok?(Newtonsoft.Json.Linq.JToken)cy:Newtonsoft.Json.Linq.JValue.CreateNull()},{"check_began_qpc",began},{"rejected_qpc",rejected},{"cursor_observed_qpc",cursorAt},{"machine_time_ms",machine},{"qpc_frequency",Stopwatch.Frequency}};
   using(var self=Process.GetCurrentProcess()){
    record["child_pid"]=self.Id;
    if(GetProcessTimes(self.Handle,out long created,out long exited,out long kernel,out long user))record["child_creation_filetime"]=created;
    else{record["child_creation_filetime"]=Newtonsoft.Json.Linq.JValue.CreateNull();record["identity_error"]=Marshal.GetLastWin32Error();}
   }
   string json=record.ToString(Newtonsoft.Json.Formatting.None);
   if(save!=null)save(path,json);else Marker(path,json);
  }catch{ /* Diagnostics cannot turn the original rejection into exit 1 or a send. */ }
  return false;
 }
 internal static bool PausePidPublication,PauseAfterCreate,OmitAtomicJob;
 static void Marker(string path,string value){string temporary=path+".tmp";byte[] bytes=Encoding.UTF8.GetBytes(value);using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}File.Move(temporary,path);}
 internal static void Run(string request,string result,Action pump,int timeout=5000,string mode="send") {
  IntPtr job=IntPtr.Zero,attributes=IntPtr.Zero,jobValue=IntPtr.Zero;bool attributesReady=false;Native.ProcessInfo child=default(Native.ProcessInfo);
  try {
   job=Native.CreateJobObjectW(IntPtr.Zero,null);Check(job!=IntPtr.Zero,"injector-job");
   var limits=new Native.Limits();limits.basic.flags=0x2000|8;limits.basic.active=1;
   IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(limits));
   try{Marshal.StructureToPtr(limits,memory,false);Check(Native.SetInformationJobObject(job,9,memory,(uint)Marshal.SizeOf(limits)),"injector-job-limits");}finally{Marshal.FreeHGlobal(memory);}
   string executable=Process.GetCurrentProcess().MainModule.FileName;
   UIntPtr size=UIntPtr.Zero;Native.InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);attributes=Marshal.AllocHGlobal(checked((int)size.ToUInt64()));Check(Native.InitializeProcThreadAttributeList(attributes,1,0,ref size),"injector-attributes");attributesReady=true;
   jobValue=Marshal.AllocHGlobal(IntPtr.Size);Marshal.WriteIntPtr(jobValue,job);Check(Native.UpdateProcThreadAttribute(attributes,0,new UIntPtr(0x2000d),jobValue,new UIntPtr((uint)IntPtr.Size),IntPtr.Zero,IntPtr.Zero),"injector-atomic-job");
   var startup=new Native.StartupEx();startup.startup.cb=(uint)Marshal.SizeOf(OmitAtomicJob?typeof(Native.Startup):typeof(Native.StartupEx));startup.attributes=OmitAtomicJob?IntPtr.Zero:attributes;
   var command=new StringBuilder("\""+executable+"\" --owned-injector \""+request+"\" \""+result+"\" "+mode);
   Check(Native.CreateProcessW(executable,command,IntPtr.Zero,IntPtr.Zero,false,OmitAtomicJob?0x08000004u:0x08080004u,IntPtr.Zero,null,ref startup,out child),"injector-create-suspended");
   Check(GetProcessTimes(child.process,out long created,out long exited,out long kernel,out long user),"injector-creation-time");
   if(PauseAfterCreate){Marker(Path.Combine(Path.GetDirectoryName(request),"creation-paused"),child.pid+" "+created);System.Threading.Thread.Sleep(30000);}
   string record=request+".pid",temporary=record+".tmp";
   using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){
    if(PausePidPublication){byte[] partial=Encoding.UTF8.GetBytes(child.pid+" ");stream.Write(partial,0,partial.Length);stream.Flush(true);Marker(Path.Combine(Path.GetDirectoryName(request),"pid-publication-paused"),child.pid+" "+created);System.Threading.Thread.Sleep(30000);stream.SetLength(0);stream.Position=0;}
    byte[] bytes=Encoding.UTF8.GetBytes(child.pid+" "+created);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
   }
   File.Move(temporary,record);
   Check(Native.ResumeThread(child.thread)!=uint.MaxValue,"injector-resume");
   var watch=Stopwatch.StartNew();
   while(Native.WaitForSingleObject(child.process,0)==258){if(watch.ElapsedMilliseconds>=timeout)throw new TimeoutException("owned-injector-timeout");pump();}
   Check(Native.WaitForSingleObject(child.process,0)==0,"injector-exit-observed");
   Check(Native.GetExitCodeProcess(child.process,out uint code),"injector-exit-code-read");
   if(code!=0)throw new InvalidOperationException("injector-exit-code-"+code);
  } finally {
   // Even a pump exception cannot leave an executor running after this method.
   if(child.process!=IntPtr.Zero){
    if(Native.WaitForSingleObject(child.process,0)!=0){
     if(job!=IntPtr.Zero)Native.TerminateJobObject(job,1);
     // Explicit negative fixture omits atomic Job membership.
     if(Native.WaitForSingleObject(child.process,100)!=0)TerminateProcess(child.process,1);
     if(Native.WaitForSingleObject(child.process,5000)!=0)Environment.FailFast("Cannot prove input injector exited; refusing desktop restoration");
    }
    Native.CloseHandle(child.process);
   }
   if(child.thread!=IntPtr.Zero)Native.CloseHandle(child.thread);
   if(job!=IntPtr.Zero)Native.CloseHandle(job);
   if(attributesReady)Native.DeleteProcThreadAttributeList(attributes);
   if(attributes!=IntPtr.Zero)Marshal.FreeHGlobal(attributes);
   if(jobValue!=IntPtr.Zero)Marshal.FreeHGlobal(jobValue);
  }
 }
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateProcess(IntPtr process,uint code);
 static void Check(bool value,string reason){if(!value)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),reason);}
}

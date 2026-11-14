using System;using System.IO;using System.Diagnostics;using System.Reflection;using System.Runtime.InteropServices;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Supervision.Windows;
// Called only from an output-local instrumented copy of the production broker.
// A real framed child reaches the real parser before this test barrier.
static class AudioRaceControl {
 internal static AudioBroker Target;internal static readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Resume=new ManualResetEventSlim();internal static int LatePublications;
 public static void Parsed(AudioBroker source,ulong sequence){if(ReferenceEquals(source,Target)&&sequence==2){Entered.Set();if(!Resume.Wait(10000))throw new TimeoutException("controlled-publish-barrier");}}
 public static void Published(AudioBroker source){if(ReferenceEquals(source,Target)&&Resume.IsSet)Interlocked.Increment(ref LatePublications);}
 internal static void Reset(){Target=null;Entered.Reset();Resume.Reset();LatePublications=0;}
}
static class AudioPublishRaceChecks {
 [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool DuplicateHandle(IntPtr source,IntPtr handle,IntPtr target,out IntPtr copy,uint access,bool inherit,uint options);
 [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")]static extern uint GetProcessId(IntPtr handle);
 [DllImport("kernel32.dll")]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static byte[] Await(AudioBroker broker,Func<byte[],bool> predicate,int limit){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<limit){var snapshot=broker.Snapshot();if(predicate(snapshot))return snapshot;Thread.Sleep(2);}throw new Exception("race-observation-deadline");}
 static void Zero(byte[] value,uint status){Need(BitConverter.ToUInt32(value,32)==status,"revoked-status-preserved");Need(BitConverter.ToUInt64(value,24)==0,"revoked-capture-zero");for(int i=36;i<40;i++)Need(value[i]==0,"revoked-features-zero");}
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static IntPtr Retain(AudioBroker broker){var child=typeof(AudioBroker).GetField("child",Hidden).GetValue(broker);var handle=(IntPtr)child.GetType().GetField("process",Hidden).GetValue(child);IntPtr copy;Need(DuplicateHandle(GetCurrentProcess(),handle,GetCurrentProcess(),out copy,0x101000,false,0),"retain-actual-child-handle");return copy;}
 static JObject Run(string executable,bool watchdog){AudioRaceControl.Reset();var row=new JObject{{"case",watchdog?"watchdog-before-late-publication":"dispose-before-late-publication"}};Console.WriteLine("CASE "+row["case"]);AudioBroker old=null,next=null;Thread dispose=null;Exception disposeError=null;IntPtr retained=IntPtr.Zero;using(var station=new PrivateStation()){try{
  old=new AudioBroker(station,executable,12,1);AudioRaceControl.Target=old;retained=Retain(old);long created,exit,kernel,user;Need(GetProcessTimes(retained,out created,out exit,out kernel,out user),"actual-child-creation");row["old_pid"]=GetProcessId(retained);row["old_created"]=created;
  Await(old,b=>b[32]==1&&b[36]>0,1000);Need(AudioRaceControl.Entered.Wait(1500),"validated-second-frame-parked-before-publication");
  if(watchdog)Await(old,b=>b[32]==5,2500);
  else{dispose=new Thread(()=>{try{old.Dispose();}catch(Exception error){disposeError=error;}}){IsBackground=true};dispose.Start();Await(old,b=>b[32]==4,1000);}
  Zero(old.Snapshot(),watchdog?5u:4u);Need(WaitForSingleObject(retained,1000)==0,"old-child-kernel-exited-before-release");
  next=new AudioBroker(station,executable,12,1);Await(next,b=>b[32]==1&&b[36]>0,1000);
  AudioRaceControl.Resume.Set();if(dispose!=null)Need(dispose.Join(2000)&&disposeError==null,"dispose-completes-with-original-join-deadline");else{var reader=(Thread)typeof(AudioBroker).GetField("reader",Hidden).GetValue(old);Need(reader.Join(2000),"terminal-reader-exited");}
  Need(AudioRaceControl.LatePublications==0,"late-validated-frame-not-published");Zero(old.Snapshot(),watchdog?5u:4u);Need(next.Snapshot()[32]==1,"old-generation-did-not-revoke-new-broker");
  row["late_publications"]=AudioRaceControl.LatePublications;row["old_kernel_exited_before_release"]=true;row["new_broker_independent"]=true;
 }finally{AudioRaceControl.Resume.Set();if(dispose!=null)Need(dispose.Join(3000),"dispose-thread-cleanup");old?.Dispose();next?.Dispose();Need(retained==IntPtr.Zero||WaitForSingleObject(retained,0)==0,"retained-child-final-exit");if(retained!=IntPtr.Zero)Need(CloseHandle(retained),"retained-handle-closed");Need(station.ChildCount==0,"all-owned-children-released");Console.WriteLine("CLEANUP all-owned-children-released");row["children_remaining"]=station.ChildCount;}}
 return row;}
 static int Main(string[] args){var rows=new JArray();try{rows.Add(Run(args[0],false));rows.Add(Run(args[0],true));File.WriteAllText(Path.Combine(args[1],"broker-results.json"),rows.ToString());return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

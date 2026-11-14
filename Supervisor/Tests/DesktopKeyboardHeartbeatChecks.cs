using System;using System.Reflection;using System.Runtime.Serialization;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Sdk.Capabilities;
// State-only keyboard invalidation checks. Owns/Publish/native forwarding are
// substituted by the runner; this never claims desktop authority or Host lease proof.
static class DesktopKeyboardHeartbeatChecks {
 sealed class Target:IDesktopInputTarget {
  public ulong CaptureEpoch=>7;public int Revocations;
  public void Observe(InputObservation value){throw new Exception("unexpected-observation");}
  public void AcquireFocus(InputObservation value){throw new Exception("unexpected-grant");}
  public void Revoke(){Revocations++;}
  public InputFocusConfirmation GetFocusConfirmation()=>new InputFocusConfirmation(7,false);
 }
 static readonly Type Type=typeof(WindowsDesktopInput);const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(object source,string name,object value)=>Type.GetField(name,Fields).SetValue(source,value);
 static object Get(object source,string name)=>Type.GetField(name,Fields).GetValue(source);
 static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 static Func<T> Query<T>(object value,ManualResetEventSlim entered,ManualResetEventSlim release)=>()=>{entered.Set();if(!release.Wait(2000))throw new TimeoutException("query-barrier");return (T)value;};
 static void Key(object source,int key,bool up,int code=0){
  var dataType=Type.GetNestedType("KeyData",BindingFlags.NonPublic);object data=Activator.CreateInstance(dataType);
  dataType.GetField("Key").SetValue(data,(uint)key);dataType.GetField("Flags").SetValue(data,up?0x80u:0u);dataType.GetField("Time").SetValue(data,unchecked((uint)WindowsMachineClock.Milliseconds()));
  IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(dataType));try{Marshal.StructureToPtr(data,memory,false);Type.GetMethod("Keyboard",Fields).Invoke(source,new object[]{code,new IntPtr(up?0x101:0x100),memory});}finally{Marshal.FreeHGlobal(memory);}
 }
 static void Case(int key,bool owns,bool active,int code,bool closed=false){
  var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);var target=new Target();Set(source,"target",target);Set(source,"fence",new InputCommitFence());Set(source,"active",active);Set(source,"closed",closed);Set(source,"acceptanceOwns",owns);
  if(key==65){Set(source,"keyHeld",16u);Set(source,"committedHeld",8u);Set(source,"sequence",23L);}
  var sampleType=Type.GetNestedType("HeartbeatSample",BindingFlags.NonPublic);var sample=Activator.CreateInstance(sampleType,true);sampleType.GetField("Time",Fields|BindingFlags.Public).SetValue(sample,123L);
  var heartbeatType=typeof(InputHeartbeat<>).MakeGenericType(sampleType);var heartbeat=Activator.CreateInstance(heartbeatType,true);Set(source,"heartbeat",heartbeat);
  using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
   var query=typeof(DesktopKeyboardHeartbeatChecks).GetMethod("Query",BindingFlags.NonPublic|BindingFlags.Static).MakeGenericMethod(sampleType).Invoke(null,new object[]{sample,entered,release});
   bool started=false;
   try{
    started=(bool)heartbeatType.GetMethod("Begin").Invoke(heartbeat,new[]{query});Need(started&&entered.Wait(1000),"query-in-flight-before-key");
    Key(source,key,false,code);if(key==65&&owns&&active&&!closed&&code==0){for(int i=0;i<32;i++)Key(source,key,false);Key(source,key,true);}
    if(key==32){Need((uint)Get(source,"keyHeld")==4&&(int)Get(source,"acceptancePublishes")==1,"mapped-down-still-updates-held-and-publishes");Key(source,key,true);Need((uint)Get(source,"keyHeld")==0&&(int)Get(source,"acceptancePublishes")==2,"mapped-up-still-clears-held-and-publishes");}
   }
   finally{release.Set();if(started)Need(((Task)heartbeatType.GetProperty("Completion").GetValue(heartbeat)).Wait(3000),"query-barrier-drained-before-events-disposed");}
   ((Task)heartbeatType.GetProperty("Completion").GetValue(heartbeat)).GetAwaiter().GetResult();object[] result={null};bool taken=(bool)heartbeatType.GetMethod("TryTake").Invoke(heartbeat,result);
   bool ignored=code!=0||key==65&&owns&&active&&!closed;
    bool counted=key==65&&owns&&active&&!closed&&code==0;
    Need((long)Get(source,"unknownKeyDowns")== (counted?33:0)&&(long)Get(source,"unknownKeyUps")== (counted?1:0),"hook-counter-records-only-actual-unknown-active-events");
   Need(taken==ignored,"key-heartbeat-validity-"+key+"-"+owns+"-"+active+"-"+code);
   if(taken)Need(ReferenceEquals(result[0],sample)&&(long)sampleType.GetField("Time",Fields|BindingFlags.Public).GetValue(sample)==123,"ignored-key-retains-original-sample-and-time");
   if(key==65){Need((int)Get(source,"acceptancePublishes")==0&&(long)Get(source,"sequence")==23,"unknown-key-never-publishes-or-renews");if(owns)Need((uint)Get(source,"keyHeld")==16&&(uint)Get(source,"committedHeld")==8,"unknown-key-preserves-nonzero-held-sentinels");}
   if(active&&!closed&&code==0)Need((int)Get(source,"acceptanceOwnsCalls")>0,"unknown-or-mapped-key-still-checks-current-ownership");
   Need((bool)Get(source,"closed")==closed,"keyboard-check-did-not-pass-by-exception-close");
   if(key!=65&&key!=32)Need(target.Revocations==1&&!(bool)Get(source,"active")&&(int)Get(source,"acceptancePublishes")==0,"modifier-revokes-without-publication");
   if(!owns&&active&&code==0)Need(target.Revocations==1&&!(bool)Get(source,"active"),"unknown-key-with-lost-ownership-revokes");
   heartbeatType.GetMethod("Close").Invoke(heartbeat,null);
  }
 }
 static void TraceIsolation(){
  var requestType=Type.GetNestedType("FocusRequest",BindingFlags.NonPublic);var first=Activator.CreateInstance(requestType,true);var second=Activator.CreateInstance(requestType,true);
  var trace=requestType.GetField("Trace",Fields);var oldTrace=(long[])trace.GetValue(first);var currentTrace=(long[])trace.GetValue(second);
  var stamp=Type.GetMethod("FocusStamp",BindingFlags.Static|BindingFlags.NonPublic);stamp.Invoke(null,new object[]{first,13});
  Need(!ReferenceEquals(oldTrace,currentTrace)&&oldTrace[13]>0&&currentTrace[13]==0,"late-focus-diagnostic-stays-in-old-request");
  stamp.Invoke(null,new object[]{second,3});Need(currentTrace[3]>0&&oldTrace[3]==0,"new-focus-diagnostic-has-independent-stage-array");
  Need(Type.GetNestedType("HeartbeatSample",BindingFlags.NonPublic).GetField("Trace",Fields)==null,"heartbeat-does-not-allocate-focus-diagnostic-array");
 }
 static int Main(){try{TraceIsolation();Case(65,true,true,0);Case(65,false,true,0);Case(65,true,false,0);Case(65,true,true,-1);Case(65,true,true,0,true);Case(32,true,true,0);foreach(int key in new[]{16,17,18,91,92,160,161,162,163,164,165})Case(key,true,true,0);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

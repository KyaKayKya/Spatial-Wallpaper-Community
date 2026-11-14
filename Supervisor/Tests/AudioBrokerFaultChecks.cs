using System;using System.Threading;using System.Diagnostics;
using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Supervision.Windows;
static class AudioBrokerFaultChecks {
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static byte[] Await(AudioBroker broker,Func<byte[],bool> predicate,int limit){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<limit){var value=broker.Snapshot();if(predicate(value))return value;Thread.Sleep(5);}throw new Exception("audio-fault-deadline");}
 static void Zero(byte[] value){for(int i=36;i<40;i++)Need(value[i]==0,"audio-fault-retained-feature");Need(BitConverter.ToUInt64(value,24)==0,"audio-fault-retained-capture-time");}
 static int Main(string[] args){try{using(var station=new PrivateStation()){
  foreach(int mode in new[]{1,2,3,4,6,7,8,9,10}){
   var elapsed=Stopwatch.StartNew();using(var broker=new AudioBroker(station,args[0],mode,1))Zero(Await(broker,p=>p[32]==5,4500));
   Need(station.ChildCount==0,"audio-fault-child-remains");Console.WriteLine("PASS fault "+mode+" "+elapsed.ElapsedMilliseconds+"ms");
  }
  using(var broker=new AudioBroker(station,args[0],5,1)){Zero(Await(broker,p=>p[32]==1,1000));Zero(Await(broker,p=>p[32]==5,2000));}
  Need(station.ChildCount==0,"stale-audio-child-remains");
  var active=new AudioBroker(station,args[0],11,1);Await(active,p=>p[32]==1&&p[36]>0,1000);var stop=Stopwatch.StartNew();active.Dispose();active.Dispose();
  Need(stop.ElapsedMilliseconds<1800,"active-dispose-deadline");Zero(active.Snapshot());Need(station.ChildCount==0,"active-audio-child-remains");
  var delayed=new AudioBroker(station,args[0],11,1);Await(delayed,p=>p[32]==1&&p[36]>0,1000);
  var reader=(Thread)typeof(AudioBroker).GetField("reader",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(delayed);
#pragma warning disable 618
  reader.Suspend();bool deadline=false;try{delayed.Dispose();}catch(InvalidOperationException e){deadline=e.Message=="audio-reader-stop-deadline";}finally{reader.Resume();}
#pragma warning restore 618
  Need(deadline,"reader suspension did not exercise disposal deadline");delayed.Dispose();Need(station.ChildCount==0,"retry-dispose-leaked-child");
 }Console.WriteLine("PASS watchdog, partial frame, malformed data, future capture, stale capture, rate, replay, EOF, active disposal, retry disposal and job cleanup");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

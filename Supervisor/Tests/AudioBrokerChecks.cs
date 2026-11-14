using System;using System.Threading;using System.Diagnostics;
using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Supervision.Windows;
static class AudioBrokerChecks {
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static byte[] Await(AudioBroker broker,Func<byte[],bool> predicate,int limit){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<limit){var value=broker.Snapshot();if(predicate(value))return value;Thread.Sleep(10);}throw new Exception("audio-broker-deadline");}
 static int Main(string[] args){try{
  using(var station=new PrivateStation()){
   using(var rejected=new AudioBroker(station,args[0],int.Parse(args[1]),ulong.Parse(args[2])+1))Await(rejected,p=>p[32]==3,4000);
   Need(station.ChildCount==0,"rejected-audio-child-remains");
   using(var broker=new AudioBroker(station,args[0],int.Parse(args[1]),ulong.Parse(args[2]))){
    Await(broker,p=>p[32]==1&&p[36]>0,4000);Console.WriteLine("ready");Console.Out.Flush();
    if(args.Length==4){if(args[3]=="crash")Thread.Sleep(Timeout.Infinite);else Console.ReadLine();}
    else{var ended=Await(broker,p=>p[32]==2,5000);for(int i=36;i<40;i++)Need(ended[i]==0,"source-ended-feature-retained");}
   }
   Need(station.ChildCount==0,"audio-child-remains");
  }
  Console.WriteLine("PASS private-station audio broker, wrong identity, bounded features, source exit and job cleanup");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

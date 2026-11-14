using System;using System.IO;using System.IO.Pipes;using System.Reflection;using System.Threading;using System.Threading.Tasks;
using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class InputChannelChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);Console.WriteLine("PASS "+code);}
 static int Main(){try{
  var type=typeof(InputBroker).Assembly.GetType("SpatialWallpaper.Supervision.Broker.PreviewInputChannel");Need(type!=null,"trusted-preview-input-channel-exists");
  using(var broker=new InputBroker())using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false)){
   string name="SpatialWallpaper.Input.Test."+Guid.NewGuid().ToString("N");
   using(var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)){
    var serving=Task.Run(()=>{server.WaitForConnection();try{type.GetMethod("Serve").Invoke(null,new object[]{server,target});}catch(TargetInvocationException e){if(!(e.InnerException is IOException)&&!(e.InnerException is TimeoutException))throw;}});
    client.Connect(1000);dynamic source=Activator.CreateInstance(type,new object[]{client});
    ulong epoch=(ulong)source.CaptureEpoch;long stamp=WindowsMachineClock.Milliseconds();
    source.Observe(new InputObservation(1,stamp,true,true,true,true,true,false,true,1,.25f,.75f,0,epoch));
    var frame=new InputFrames();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused&&frame.GetButtonDown("Primary"),"channel-publishes-through-host-gate");
    epoch=(ulong)source.CaptureEpoch;source.Observe(new InputObservation(2,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,false,4,.25f,.75f,120,epoch));
    frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.GetButtonDown("Space")&&frame.Wheel==1,"channel-preserves-author-state");
    // A timer proof can arrive before an already captured native message.
    // Heartbeats must cross the real IPC codec without ordering raw edges.
    Thread.Sleep(5);long captured=WindowsMachineClock.Milliseconds();Thread.Sleep(10);
    source.Observe(new InputObservation(3,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,false,4,.25f,.75f,0,epoch,true));
    frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused&&frame.GetButton("Space")&&!frame.GetButtonDown("Space"),"channel-heartbeat-does-not-repeat-edge");
    source.Observe(new InputObservation(4,captured,true,true,true,true,true,false,false,0,.25f,.75f,0,epoch));
    frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused&&frame.GetButtonUp("Space")&&!frame.GetButton("Space"),"channel-heartbeat-does-not-overtake-captured-native-edge");
    bool duplicate=false;try{type.GetMethod("Serve").Invoke(null,new object[]{Stream.Null,target});}catch(TargetInvocationException e){duplicate=e.InnerException is InvalidOperationException;}Need(duplicate,"duplicate-source-lease-rejected");
    frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused&&!frame.GetButton("Space"),"rejected-duplicate-does-not-revoke-owner");
    source.Revoke();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!frame.Focused,"explicit-revoke-clears-host");
    source.Observe(new InputObservation(5,stamp,true,true,true,true,true,false,true,1,.25f,.75f,0,epoch));frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!frame.Focused,"old-capture-epoch-cannot-reacquire");
    epoch=(ulong)source.CaptureEpoch;source.Observe(new InputObservation(6,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,true,1,.25f,.75f,0,epoch));frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused,"fresh-click-after-revoke");
    ((IDisposable)source).Dispose();Need(serving.Wait(1000),"disconnect-reader-bounded");serving.GetAwaiter().GetResult();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!frame.Focused,"disconnect-clears-host-input");
   }
   // A fresh trusted connection gets a new epoch and sequence namespace.
   // It must not replay the previous owner's held state or queued observations.
   string nextName="SpatialWallpaper.Input.Test."+Guid.NewGuid().ToString("N");
   using(var server=new NamedPipeServerStream(nextName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))using(var client=new NamedPipeClientStream(".",nextName,PipeDirection.InOut,PipeOptions.Asynchronous)){
    ulong oldEpoch=target.CaptureEpoch;
    var serving=Task.Run(()=>{server.WaitForConnection();try{type.GetMethod("Serve").Invoke(null,new object[]{server,target});}catch(TargetInvocationException e){if(!(e.InnerException is IOException)&&!(e.InnerException is TimeoutException))throw;}});
    client.Connect(1000);dynamic source=Activator.CreateInstance(type,new object[]{client});Need((ulong)source.CaptureEpoch>oldEpoch,"reconnect-rotates-source-epoch");
    source.Observe(new InputObservation(1,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,true,1,.25f,.75f,0,(ulong)source.CaptureEpoch));
    var frame=new InputFrames();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused,"reconnected-source-sequence-one-acquires");
    Need(serving.Wait(1000),"stalled-source-deadline-bounded");serving.GetAwaiter().GetResult();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!frame.Focused,"stalled-source-revokes-host");((IDisposable)source).Dispose();
   }
   string invalidName="SpatialWallpaper.Input.InvalidKeepAlive."+Guid.NewGuid().ToString("N");
   using(var server=new NamedPipeServerStream(invalidName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))using(var client=new NamedPipeClientStream(".",invalidName,PipeDirection.InOut,PipeOptions.Asynchronous)){
    var serving=Task.Run(()=>{server.WaitForConnection();try{PreviewInputChannel.Serve(server,target);return "unexpected-success";}catch(InvalidDataException error){return error.Message;}finally{server.Dispose();}});
    client.Connect(1000);using(var source=new PreviewInputChannel(client)){
     source.Observe(new InputObservation(1,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,true,1,.25f,.75f,0,source.CaptureEpoch));var frame=new InputFrames();frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frame.Focused,"malformed-keepalive-same-session-positive");
     var malformed=new byte[56];malformed[0]=1;malformed[4]=3;malformed[8]=1;var write=client.WriteAsync(malformed,0,malformed.Length);Need(write.Wait(1000),"malformed-keepalive-write-bounded");write.GetAwaiter().GetResult();Need(serving.Wait(1000)&&serving.Result=="preview-input-control-shape","nonzero-keepalive-payload-rejected");frame.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!frame.Focused&&!frame.GetButton("Primary"),"malformed-keepalive-detaches-source-and-clears-authority");
    }
   }
   using(var desktop=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true)){
    bool rejected=false;try{type.GetMethod("Serve").Invoke(null,new object[]{Stream.Null,desktop});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}Need(rejected,"preview-channel-cannot-own-desktop-target");
   }
  }return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

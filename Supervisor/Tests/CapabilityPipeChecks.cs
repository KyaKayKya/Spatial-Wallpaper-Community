using System;using System.IO;using System.Linq;using System.Threading;using System.Diagnostics;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Capabilities;
static class CapabilityPipeChecks {
 sealed class Peer:IDisposable {
  internal RestrictedProcess Child;internal CapabilityServer Server;internal WorkerWritePipe Log;internal OwnedAttempt Workspace;
  public void Dispose(){Child?.Kill(124);if(Child!=null&&Child.Wait(5000)!=0)throw new Exception("capability-peer-still-alive");Server?.Dispose();Child?.Dispose();Log?.Dispose();Workspace?.Dispose();}
 }
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static readonly JArray inputObservations=new JArray();
 static void InputCase(Peer peer,InputTarget target,bool allowed,bool omitRevoke){
  long sequence=0,clearedAt=0;Exception readerError=null;Thread reader=null;
  using(var held=new ManualResetEventSlim())using(var cleared=new ManualResetEventSlim())try{
   if(allowed){reader=new Thread(()=>{try{
    var first=new byte[1];peer.Log.ReadExact(first);Need(first[0]=='H',"input-held-marker");held.Set();
    var result=new byte[9];peer.Log.ReadExact(result);Need(result[0]=='C',"input-clear-marker");clearedAt=BitConverter.ToInt64(result,1);cleared.Set();
   }catch(Exception error){readerError=error;held.Set();cleared.Set();}}){IsBackground=true};reader.Start();}
   Func<uint,bool,InputObservation> observation=(keys,click)=>new InputObservation(++sequence,SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,click,keys,.25f,.75f,0,target.CaptureEpoch);
   target.AcquireFocus(observation(0,true));var source=Stopwatch.StartNew();
   while(allowed?!held.IsSet:source.ElapsedMilliseconds<800){Need(source.ElapsedMilliseconds<2000,"input-held-confirmation-timeout");target.Observe(observation(4,false));Thread.Sleep(10);}
   if(readerError!=null)throw readerError;
   // Establish fresh evidence immediately before revoke. Natural expiry takes
   // 250ms; the child must confirm clear within 200ms on the shared OS clock.
   var finalEvidence=observation(4,false);target.Observe(finalEvidence);if(!omitRevoke)target.Revoke();
   if(allowed){Need(cleared.Wait(2000),"input-clear-confirmation-timeout");if(readerError!=null)throw readerError;
    long elapsed=clearedAt-finalEvidence.Time;Need(elapsed>=0&&elapsed<200,"input-revoke-only-cleared-by-natural-expiry:"+elapsed);
    inputObservations.Add(new JObject{{"case","input-active-revoke"},{"latest_evidence_to_child_clear_ms",elapsed}});
   }
   Need(peer.Child.Wait(5000)==0&&peer.Child.ExitCode==0&&!peer.Server.ProtocolFault,"LPAC-input-push-failed:"+allowed);
   Need(allowed?peer.Server.SentInputs>0:peer.Server.SentInputs==0,"input-capability-not-enforced");
   if(!allowed)inputObservations.Add(new JObject{{"case","input-denied"},{"sent_frames",peer.Server.SentInputs}});
  }finally{if(reader!=null&&!reader.Join(100)){peer.Child.Kill(124);Need(peer.Child.Wait(5000)==0&&reader.Join(1000),"input-observer-still-running");}}
 }
 static Peer Start(PrivateStation station,WorkerImage image,string attempts,BrokerSession session,StorageBroker storage,Stopwatch clock,string mode,AudioBroker audio=null,InputTarget input=null){
  var p=new Peer{Workspace=new OwnedAttempt(attempts,session.Attempt)};try{
   image.Copy(p.Workspace.Root);string boundary=WindowsSandboxCanaries.Create(attempts,p.Workspace);var args=new[]{"0","0","0",session.Attempt.ToString("N"),session.Instance.ToString("N"),mode,Path.Combine(boundary,"aap"),Path.Combine(boundary,"arap")};IntPtr output=IntPtr.Zero;
   p.Child=RestrictedProcess.Start(station,Path.Combine(p.Workspace.Root,"Peer.exe"),args,p.Workspace.Root,128L*1024*1024,sid=>{p.Server=new CapabilityServer(sid,session,storage,()=>clock.ElapsedMilliseconds,null,audio,input);p.Log=new WorkerWritePipe(sid);output=p.Log.ChildWriter;args[0]=p.Server.ChildRequestWriter.ToInt64().ToString();args[1]=p.Server.ChildReplyReader.ToInt64().ToString();args[2]=output.ToInt64().ToString();return new[]{p.Server.ChildRequestWriter,p.Server.ChildReplyReader,output};},pid=>{p.Server.CloseChildCopies();p.Log.CloseChildCopy();},session.Attempt,()=>output);p.Server.Start();return p;
  }catch{p.Dispose();throw;}
 }
 static int Main(string[] args){try{
  var hashes=new Dictionary<string,string>();foreach(var property in ((JObject)JObject.Parse(File.ReadAllText(args[1]))["files"]).Properties())hashes.Add(property.Name,(string)property.Value);var image=new WorkerImage(args[0],"Peer.exe",hashes);Directory.CreateDirectory(args[2]);
  var clock=Stopwatch.StartNew();using(var journal=new FaultJournal(Path.Combine(args[3],"journal"))){Guid installation=Guid.NewGuid();Func<BrokerSession> session=()=>new BrokerSession(journal.Begin(installation,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"private-storage"});
   var storage=new StorageBroker(Path.Combine(args[3],"store"));storage.Initialized.GetAwaiter().GetResult();try{using(var station=new PrivateStation()){
    using(var writer=Start(station,image,args[2],session(),storage,clock,"write"))Need(writer.Child.Wait(5000)==0&&writer.Child.ExitCode==0&&!writer.Server.ProtocolFault,"LPAC-write-failed");
    foreach(bool allowed in new[]{true,false})using(var input=new InputBroker()){
     var inputSession=new BrokerSession(journal.Begin(installation,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,allowed?new[]{"private-storage","input"}:new[]{"private-storage"});
     using(var wrong=input.Register(new SpatialWallpaper.Sdk.Hosting.WorkerIdentity(Guid.NewGuid(),inputSession.Instance,inputSession.Generation),true)){
      bool mismatch=false;try{using(var invalid=new CapabilityServer("unused",inputSession,storage,()=>clock.ElapsedMilliseconds,null,null,wrong)){} }catch(ArgumentException error){mismatch=error.Message=="input-target-session-mismatch";}Need(mismatch,"input-target-attempt-mismatch-not-rejected");
     }
     using(var target=input.Register(new SpatialWallpaper.Sdk.Hosting.WorkerIdentity(inputSession.Attempt,inputSession.Instance,inputSession.Generation),true))
     using(var peer=Start(station,image,args[2],inputSession,storage,clock,allowed?"input":"input-denied",null,target)){
      InputCase(peer,target,allowed,args.Length>6&&args[6]=="--negative-revoke");
     }
    }
    foreach(bool allowed in new[]{true,false})using(var audio=new AudioBroker(station,args[5],12,1)){
     var audioSession=new BrokerSession(journal.Begin(installation,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,allowed?new[]{"private-storage","audio-features"}:new[]{"private-storage"});
     using(var peer=Start(station,image,args[2],audioSession,storage,clock,allowed?"audio":"audio-denied",audio))Need(peer.Child.Wait(5000)==0&&peer.Child.ExitCode==0&&!peer.Server.ProtocolFault,"LPAC-audio-push-failed:"+allowed);
    }
    using(var sharedClient=Start(station,image,args[2],session(),storage,clock,"sdk-client")){
     Need(sharedClient.Child.Wait(10000)==0,"SDK-client-did-not-exit");var buffer=new byte[8192];int used=0,n;while((n=sharedClient.Log.Read(buffer,used,buffer.Length-used))>0){used+=n;if(used==buffer.Length)break;}string log=System.Text.Encoding.UTF8.GetString(buffer,0,used);File.WriteAllText(Path.Combine(args[3],"sdk-client.log"),log);Need(sharedClient.Child.ExitCode==0&&!sharedClient.Server.ProtocolFault,"shared-SDK-client-native-read-or-stop-failed:"+log);
    }
    var observations=new JArray();foreach(string attack in new[]{"wrong","partial"})using(var healthy=Start(station,image,args[2],session(),storage,clock,"healthy"))using(var bad=Start(station,image,args[2],session(),storage,clock,attack)){
     var watch=Stopwatch.StartNew();while(!bad.Server.ProtocolFault&&watch.ElapsedMilliseconds<5000)Thread.Sleep(2);Need(bad.Server.ProtocolFault,"malformed-peer-not-detected:"+attack);long detected=watch.ElapsedMilliseconds;long before=healthy.Server.SentReplies;var kill=Stopwatch.StartNew();bad.Child.Kill(124);Need(bad.Child.Wait(5000)==0,"bad-peer-not-terminated");long kernelExit=kill.ElapsedMilliseconds;Need(healthy.Child.Wait(10000)==0&&healthy.Child.ExitCode==0&&!healthy.Server.ProtocolFault&&healthy.Server.SentReplies>before+10,"other-capability-session-did-not-continue");observations.Add(new JObject{{"attack",attack},{"detected_since_launch_check_ms",detected},{"kill_to_kernel_exit_ms",kernelExit},{"healthy_replies_after_fault",healthy.Server.SentReplies-before}});
    }
    foreach(var observation in inputObservations)observations.Add(observation);
    Need(station.ChildCount==0,"capability-process-ownership-leak");File.WriteAllText(args[4],observations.ToString());
   }}finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  Console.WriteLine("PASS real LPAC bidirectional capability pipes, scoped storage, wrong identity and partial-header timeout isolation");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

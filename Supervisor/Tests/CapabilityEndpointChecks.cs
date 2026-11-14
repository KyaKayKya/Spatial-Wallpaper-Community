using System;using System.IO;using System.Text;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class CapabilityEndpointChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static byte[] Header(BrokerSession session,CapabilityOperation op,long sequence,int id,byte[] body){var bytes=new byte[64];CapabilityWire.Encode(bytes,new WorkerIdentity(session.Attempt,session.Instance,session.Generation),op,false,false,id,sequence,body.Length);return bytes;}
 static BrokerReply Reply(CapabilityEndpoint scope){var clock=System.Diagnostics.Stopwatch.StartNew();BrokerReply reply;while(!scope.TryTake(out reply)){if(clock.ElapsedMilliseconds>5000)throw new Exception("reply-timeout");Thread.Sleep(2);}return reply;}
 static int Main(string[] args){try{
  using(var journal=new FaultJournal(Path.Combine(args[0],"journal"))){
   Func<Guid,string[],BrokerSession> create=(install,caps)=>new BrokerSession(journal.Begin(install,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,caps);
   var storage=new StorageBroker(Path.Combine(args[0],"storage"));storage.Initialized.GetAwaiter().GetResult();
   try{
    Guid install=Guid.NewGuid();var a=create(install,new[]{"private-storage"});var b=create(install,new[]{"private-storage"});var one=new CapabilityEndpoint(a,storage);var two=new CapabilityEndpoint(b,storage);
    byte[] write=Encoding.UTF8.GetBytes("{\"version\":\"0\",\"changes\":{\"save\":\"Kg==\"}}");
    Need(one.Receive(Header(a,CapabilityOperation.StorageCommit,1,1,write),write,write.Length),"commit-receive");var done=Reply(one);Need(!done.Error&&(string)JObject.Parse(Encoding.UTF8.GetString(done.Payload))["version"]=="1","commit-result");
    byte[] read=Encoding.UTF8.GetBytes("{\"key\":\"save\"}");Need(two.Receive(Header(b,CapabilityOperation.StorageRead,1,1,read),read,read.Length),"read-receive");Need((string)JObject.Parse(Encoding.UTF8.GetString(Reply(two).Payload))["value"]=="Kg==","cross-screen-read");
    Need(!one.Receive(Header(a,CapabilityOperation.StorageRead,2,1,read),read,read.Length)&&one.Faulted,"request-id-replay-accepted");
    var foreign=create(Guid.NewGuid(),new[]{"private-storage"});var isolated=new CapabilityEndpoint(foreign,storage);Need(isolated.Receive(Header(foreign,CapabilityOperation.StorageRead,1,1,read),read,read.Length),"foreign-read");Need(JObject.Parse(Encoding.UTF8.GetString(Reply(isolated).Payload))["value"].Type==JTokenType.Null,"foreign-data-leak");
    var deniedSession=create(Guid.NewGuid(),new string[0]);var denied=new CapabilityEndpoint(deniedSession,storage);Need(!denied.Receive(Header(deniedSession,CapabilityOperation.StorageRead,1,1,read),read,read.Length)&&denied.Faulted,"undeclared-capability-accepted");
    var malformedSession=create(install,new[]{"private-storage"});var malformed=new CapabilityEndpoint(malformedSession,storage);byte[] forged=Encoding.UTF8.GetBytes("{\"key\":\"save\",\"installation\":\"victim\"}");Need(!malformed.Receive(Header(malformedSession,CapabilityOperation.StorageRead,1,1,forged),forged,forged.Length)&&malformed.Faulted,"payload-identity-accepted");
    var wrong=create(install,new[]{"private-storage"});var wrongScope=new CapabilityEndpoint(wrong,storage);Need(!wrongScope.Receive(Header(b,CapabilityOperation.StorageRead,2,2,read),read,read.Length)&&wrongScope.Faulted,"cross-instance-header-accepted");
    var llmSession=create(install,new[]{"llm-proxy"});var llm=new CapabilityEndpoint(llmSession,storage);byte[] prompt=Encoding.UTF8.GetBytes("{\"prompt\":\"arbitrary text including https://example.invalid is only data\"}");Need(llm.Receive(Header(llmSession,CapabilityOperation.LlmSubmit,1,1,prompt),prompt,prompt.Length),"reserved-service-message");var unavailable=Reply(llm);Need(unavailable.Error&&(string)JObject.Parse(Encoding.UTF8.GetString(unavailable.Payload))["error"]=="Unavailable","unimplemented-service-pretended-success");
    two.Close();Need(!two.Receive(Header(b,CapabilityOperation.StorageRead,2,2,read),read,read.Length),"closed-scope-accepted");
    var stoppingSession=create(Guid.NewGuid(),new[]{"private-storage"});var stopping=new CapabilityEndpoint(stoppingSession,storage);
    var reserved=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);var proceed=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    CapabilityEndpoint.AfterReceiveReserved=scope=>{if(ReferenceEquals(scope,stopping)){reserved.TrySetResult(true);proceed.Task.GetAwaiter().GetResult();}};
    var incoming=System.Threading.Tasks.Task.Run(()=>stopping.Receive(Header(stoppingSession,CapabilityOperation.StorageRead,1,1,read),read,read.Length));
    try{Need(reserved.Task.Wait(2000),"stop-request-is-in-flight");stopping.Close();}finally{proceed.TrySetResult(true);CapabilityEndpoint.AfterReceiveReserved=null;}
    Need(incoming.Wait(2000)&&!incoming.Result&&!stopping.Faulted,"stop-in-flight-rejection-not-protocol-fault");Need(!stoppingSession.Allows("private-storage"),"stop-session-revoked");
    var lateSession=create(Guid.NewGuid(),new[]{"private-storage"});var late=new CapabilityEndpoint(lateSession,storage);var readReady=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);var release=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    var finished=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);CapabilityEndpoint.AfterStorageFinish=scope=>{if(ReferenceEquals(scope,late))finished.TrySetResult(true);};
    StorageBroker.BeforeReadReply=(owner,key,result)=>{if(ReferenceEquals(owner,lateSession)){readReady.TrySetResult(true);release.Task.GetAwaiter().GetResult();}};
    try{Need(late.Receive(Header(lateSession,CapabilityOperation.StorageRead,1,1,read),read,read.Length),"late-read-accepted-before-stop");Need(readReady.Task.Wait(2000),"late-read-result-ready");late.Close();}finally{release.TrySetResult(true);StorageBroker.BeforeReadReply=null;}
    try{Need(finished.Task.Wait(2000),"late-real-storage-continuation-completed");}finally{CapabilityEndpoint.AfterStorageFinish=null;}
    BrokerReply ignored;Need(!late.TryTake(out ignored)&&!late.Faulted,"late-stop-result-never-published");
    var raceSession=create(Guid.NewGuid(),new[]{"private-storage"});var race=new CapabilityEndpoint(raceSession,storage);var faultEntered=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);var faultContinue=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    CapabilityEndpoint.BeforeFault=scope=>{if(ReferenceEquals(scope,race)){faultEntered.TrySetResult(true);faultContinue.Task.GetAwaiter().GetResult();}};
    var badBody=Encoding.UTF8.GetBytes("{\"wrong\":\"field\"}");var faulting=System.Threading.Tasks.Task.Run(()=>race.Receive(Header(raceSession,CapabilityOperation.StorageRead,1,1,badBody),badBody,badBody.Length));
    try{Need(faultEntered.Task.Wait(2000),"fault-publication-reached");race.Close();}finally{faultContinue.TrySetResult(true);CapabilityEndpoint.BeforeFault=null;}
    Need(faulting.Wait(2000)&&!faulting.Result&&!race.Faulted,"stop-fault-publication-race");
   }finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  Console.WriteLine("PASS scoped capability routing, opaque identity, replay/unknown-field/undeclared-capability rejection");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

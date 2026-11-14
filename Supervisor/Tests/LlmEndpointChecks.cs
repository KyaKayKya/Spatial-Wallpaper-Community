using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Broker;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;
static class LlmEndpointChecks {
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static byte[] Header(BrokerSession session,byte[] body){var bytes=new byte[64];CapabilityWire.Encode(bytes,new WorkerIdentity(session.Attempt,session.Instance,session.Generation),CapabilityOperation.LlmSubmit,false,false,1,1,body.Length);return bytes;}
 public static void Run(string origin,string root,FaultJournal journal){
  var storage=new StorageBroker(Path.Combine(root,"endpoint-storage"));storage.Initialized.GetAwaiter().GetResult();
  var broker=new LlmBroker(new LlmConfiguration(new Uri(origin+"/ok"),"fixed-model","test-secret-never-in-result",5000));
  Guid installation=Guid.NewGuid(),instance=Guid.NewGuid();
  var oldReceipt=journal.Begin(installation,new string('a',64),instance,1,StartIntent.Manual);var oldSession=new BrokerSession(oldReceipt,false,new[]{"llm-proxy"});
  var oldEndpoint=new CapabilityEndpoint(oldSession,storage,broker);CapabilityEndpoint newEndpoint=null;
  var held=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  var oldFinished=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var newFinished=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);int terminals=0;
  LlmBroker.AfterResult=()=>{if(Interlocked.Increment(ref terminals)==1){held.TrySetResult(true);if(!release.Task.Wait(3000))throw new Exception("old-result-release-missing");}};
  CapabilityEndpoint.AfterLanguageFinish=endpoint=>{if(ReferenceEquals(endpoint,oldEndpoint))oldFinished.TrySetResult(true);if(ReferenceEquals(endpoint,newEndpoint))newFinished.TrySetResult(true);};
  byte[] body=Encoding.UTF8.GetBytes("{\"prompt\":\"fixed old generation reply\"}");
  try{
   Need(oldEndpoint.Receive(Header(oldSession,body),body,body.Length),"old-endpoint-accepted");Need(held.Task.Wait(3000),"old-success-committed-before-close");
   oldEndpoint.Close();oldEndpoint.Close();Need(oldEndpoint.Closed,"old-endpoint-closed");
   journal.MarkStopping(oldReceipt);journal.Complete(oldReceipt);var newSession=new BrokerSession(journal.Begin(installation,new string('a',64),instance,2,StartIntent.Manual),false,new[]{"llm-proxy"});newEndpoint=new CapabilityEndpoint(newSession,storage,broker);
   Need(newEndpoint.Receive(Header(newSession,body),body,body.Length),"same-instance-new-generation-accepted");Need(newFinished.Task.Wait(3000),"new-generation-result-published");
   BrokerReply current;Need(newEndpoint.TryTake(out current)&&!current.Error&&current.RequestId==1,"new-generation-own-result");
   var payload=JObject.Parse(Encoding.UTF8.GetString(current.Payload));Need(Encoding.UTF8.GetString(Convert.FromBase64String((string)payload["value"]))=="reply-as-data","new-generation-fixed-text");
   release.TrySetResult(true);Need(oldFinished.Task.Wait(3000),"old-result-continuation-finished-after-new-generation");
   BrokerReply stale;Need(!oldEndpoint.TryTake(out stale)&&!newEndpoint.TryTake(out stale),"old-success-never-published-to-closed-or-new-endpoint");
   }finally{
    try{release.TrySetResult(true);oldEndpoint.Close();newEndpoint?.Close();broker.Dispose();Need(broker.Completion.Wait(3000),"endpoint-broker-completed");}
    finally{LlmBroker.AfterResult=null;CapabilityEndpoint.AfterLanguageFinish=null;storage.Close();Need(storage.Completion.Wait(3000),"endpoint-storage-completed");}
  }
 }
}

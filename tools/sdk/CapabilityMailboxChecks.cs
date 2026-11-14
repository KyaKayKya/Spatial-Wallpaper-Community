using System;using System.IO;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Execution;
static class CapabilityMailboxChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static void Reject(Action body){bool rejected=false;try{body();}catch(InvalidDataException){rejected=true;}Need(rejected,"invalid-client-reply-accepted");}
 static int Main(){try{
  var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);bool budget=false;try{new CapabilityMailbox(new ResourceLedger(new ResourceCost(128,0,0,0)),identity);}catch(ResourceBudgetException){budget=true;}Need(budget,"client-startup-unbudgeted");
  var ledger=new ResourceLedger(new ResourceCost(8*1024*1024,0,0,0));var mailbox=new CapabilityMailbox(ledger,identity);byte[] body={1,2,3};
  var saturated=new CapabilityMailbox(ledger,identity);for(int i=0;i<8;i++)Need(saturated.Begin(CapabilityOperation.LlmSubmit,body)>0,"llm-fill");Need(saturated.Begin(CapabilityOperation.LlmSubmit,body)==0,"llm-unbounded");Need(saturated.Begin(CapabilityOperation.LlmCancel,body)>0,"full-LLM-queue-cannot-cancel");Need(saturated.Begin(CapabilityOperation.LlmCancel,body)==0,"unbounded-cancel-lane");saturated.Close();
  for(int i=1;i<=8;i++)Need(mailbox.Begin(CapabilityOperation.StorageRead,body)==i,"request-id-or-capacity");Need(mailbox.Begin(CapabilityOperation.StorageRead,body)==0,"unbounded-pending");
  for(int i=1;i<=8;i++){Need(mailbox.TakeRequest(out var request)&&request.Id==i&&request.Sequence==i,"send-order");Need(request.Bytes==3&&request.Buffer[0]==1,"request-copy");}
  var header=new byte[64];CapabilityWire.Encode(header,identity,CapabilityOperation.StorageCommit,true,false,2,1,body.Length);Reject(()=>mailbox.Receive(header,body,body.Length));
  CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,true,false,2,1,body.Length);mailbox.Receive(header,body,body.Length);Need(mailbox.State(2)==3,"reply-not-ready");
  CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,true,false,1,2,body.Length);mailbox.Receive(header,body,body.Length);CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,true,false,2,3,body.Length);Reject(()=>mailbox.Receive(header,body,body.Length));
  Need(mailbox.TakeReply(2,out var reply)&&reply.RequestId==2&&reply.Data[0]==1,"take-response");Need(mailbox.Begin(CapabilityOperation.StorageRead,body)==9,"request-id-reused");
  CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,true,false,0,3,body.Length);mailbox.Receive(header,body,body.Length);Need(mailbox.TakePush(CapabilityOperation.InputSnapshot,out var push)&&push.Data[2]==3,"push-not-buffered");
  long charged=ledger.Used.ManagedBytes;mailbox.Close();Need(mailbox.Begin(CapabilityOperation.StorageRead,body)==0&&ledger.Used.ManagedBytes==charged,"logical-close-refunded-live-buffers");Need(mailbox.TakeReply(1,out var cancelled)&&cancelled.Error,"close-did-not-cancel");
  Console.WriteLine("PASS client mailbox budgets, fixed pending bound, reply ownership, out-of-order completion and close");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

using System;using System.IO;using System.Text;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Capabilities {
 public sealed class CapabilityResponse {
  public CapabilityOperation Operation{get;}public int RequestId{get;}public bool Error{get;}internal readonly byte[] Data;
  internal CapabilityResponse(CapabilityOperation operation,int id,bool error,byte[] payload){Operation=operation;RequestId=id;Error=error;Data=payload;}
 }
 public sealed class CapabilitySend {
  public CapabilityOperation Operation{get;internal set;}public int Id{get;internal set;}public long Sequence{get;internal set;}public int Bytes{get;internal set;}
  internal readonly byte[] Buffer=new byte[CapabilityWire.MaxPayloadBytes];internal readonly byte[] Response=new byte[CapabilityWire.MaxPayloadBytes];internal int Phase,ResponseBytes;internal bool Error;
 }
 // Fixed buffers and credits. Response data belongs to this exact attempt/instance/generation.
 public sealed class CapabilityMailbox {
  public const int Capacity=8;const int Slots=Capacity+1;const long LayoutBytes=(Slots*2L+3)*CapabilityWire.MaxPayloadBytes+32768;
  readonly object gate=new object();readonly ResourceLedger ledger;readonly WorkerIdentity identity;readonly CapabilitySend[] slots;readonly byte[][] pushes;
  readonly int[] queue=new int[Slots],pushBytes=new int[3];readonly byte[] shapeHeader=new byte[64];int head,count,nextId;long outgoing,incoming;bool closed;
  public CapabilityMailbox(ResourceLedger accounting,WorkerIdentity session){
   ledger=accounting??throw new ArgumentNullException(nameof(accounting));identity=session??throw new ArgumentNullException(nameof(session));var memory=new ManagedReservation(ledger,LayoutBytes);
   slots=new CapabilitySend[Slots];for(int i=0;i<slots.Length;i++)slots[i]=new CapabilitySend();pushes=new byte[3][];for(int i=0;i<pushes.Length;i++)pushes[i]=new byte[CapabilityWire.MaxPayloadBytes];memory.Bind(this);
  }
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  CapabilitySend Find(int id){foreach(var slot in slots)if(slot.Phase!=0&&slot.Id==id)return slot;return null;}
  public int Begin(CapabilityOperation operation,byte[] payload){
   if(payload==null)throw new ArgumentNullException(nameof(payload));lock(gate){
    if(closed)return 0;bool control=operation==CapabilityOperation.LlmCancel;int occupied=0;foreach(var candidate in slots)if(candidate.Phase!=0&&(candidate.Operation==CapabilityOperation.LlmCancel)==control)occupied++;if(occupied>=(control?1:Capacity))return 0;int index=Array.FindIndex(slots,slot=>slot.Phase==0);if(index<0)return 0;int id=checked(nextId+1);CapabilityWire.Encode(shapeHeader,identity,operation,false,false,id,1,payload.Length);
    var slot=slots[index];Buffer.BlockCopy(payload,0,slot.Buffer,0,payload.Length);slot.Operation=operation;slot.Id=id;slot.Bytes=payload.Length;slot.Phase=1;slot.ResponseBytes=0;slot.Error=false;queue[(head+count)%Slots]=index;count++;nextId=id;return id;
   }
  }
  internal bool TakeRequest(out CapabilitySend request){lock(gate){if(closed||count==0){request=null;return false;}request=slots[queue[head]];head=(head+1)%Slots;count--;Need(request.Phase==1,"capability-send-state");request.Sequence=checked(++outgoing);request.Phase=2;return true;}}
  public int State(int id){lock(gate)return Find(id)?.Phase??0;}
  public void Receive(byte[] header,byte[] payload,int length){lock(gate){
   if(closed)return;var message=CapabilityWire.Validate(header,identity,true,incoming);Need(payload!=null&&length==message.PayloadBytes&&length<=payload.Length,"capability-reply-body-length");
   if(message.RequestId==0){int index=(int)message.Operation-100;Need(index>=0&&index<3,"capability-push-kind");Buffer.BlockCopy(payload,0,pushes[index],0,length);pushBytes[index]=length;}
   else{var slot=Find(message.RequestId);Need(slot!=null&&slot.Phase==2&&slot.Operation==message.Operation,"capability-reply-ownership");Buffer.BlockCopy(payload,0,slot.Response,0,length);slot.ResponseBytes=length;slot.Error=message.Error;slot.Phase=message.Error?4:3;}incoming=message.Sequence;
  }}
  CapabilityResponse Copy(CapabilityOperation operation,int id,bool error,byte[] bytes,int length){
   var memory=new ManagedReservation(ledger,checked(length+256L));var value=new byte[length];Buffer.BlockCopy(bytes,0,value,0,length);var result=new CapabilityResponse(operation,id,error,value);memory.Bind(result);return result;
  }
  public bool TakeReply(int id,out CapabilityResponse result){lock(gate){
   var slot=Find(id);if(slot==null||slot.Phase<3){result=null;return false;}
   if(slot.Phase==5){byte[] cancelled=Encoding.UTF8.GetBytes("{\"version\":\"0\",\"value\":null,\"error\":\"Closed\"}");result=Copy(slot.Operation,id,true,cancelled,cancelled.Length);}else result=Copy(slot.Operation,id,slot.Error,slot.Response,slot.ResponseBytes);slot.Phase=0;return true;
  }}
  public bool TakePush(CapabilityOperation operation,out CapabilityResponse result){lock(gate){int index=(int)operation-100;Need(index>=0&&index<3,"capability-push-kind");if(closed||pushBytes[index]==0){result=null;return false;}result=Copy(operation,0,false,pushes[index],pushBytes[index]);pushBytes[index]=0;return true;}}
  public void Close(){lock(gate){closed=true;count=0;foreach(var slot in slots)if(slot.Phase!=0)slot.Phase=5;Array.Clear(pushBytes,0,pushBytes.Length);}}
 }
}

using System;using System.IO;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Capabilities {
 public enum CapabilityOperation:ushort {StorageRead=1,StorageCommit=2,LlmSubmit=10,LlmCancel=11,SoundPcm=20,InputSnapshot=100,AudioSnapshot=101,ViewSnapshot=102}
 public readonly struct CapabilityHeader {
  public readonly CapabilityOperation Operation;public readonly long Sequence;public readonly int RequestId,PayloadBytes;public readonly bool Error;
  internal CapabilityHeader(CapabilityOperation operation,long sequence,int id,int bytes,bool error){Operation=operation;Sequence=sequence;RequestId=id;PayloadBytes=bytes;Error=error;}
 }
 // Separate from pixels and management. Pipe ownership selects the installation; peer fields cannot select another session.
 public static class CapabilityWire {
  public const int HeaderBytes=64,MaxPayloadBytes=65536;const uint Magic=0x50435753;
  static void Need(bool value,string code){if(!value)throw new InvalidDataException(code);}
  static void Put(byte[] bytes,int offset,ulong value,int count){for(int i=0;i<count;i++)bytes[offset+i]=(byte)(value>>(8*i));}
  static uint U32(byte[] bytes,int offset)=>(uint)bytes[offset]|(uint)bytes[offset+1]<<8|(uint)bytes[offset+2]<<16|(uint)bytes[offset+3]<<24;
  static ulong U64(byte[] bytes,int offset){ulong value=0;for(int i=0;i<8;i++)value|=(ulong)bytes[offset+i]<<(8*i);return value;}
  static bool Match(byte[] data,int offset,byte[] expected){int difference=0;for(int i=0;i<16;i++)difference|=data[offset+i]^expected[i];return difference==0;}
  static void Shape(CapabilityOperation operation,bool toWorker,bool error,int request,int payload){
   Need(payload>0&&payload<=MaxPayloadBytes,"capability-payload-limit");
   switch(operation){
    case CapabilityOperation.StorageRead:case CapabilityOperation.StorageCommit:case CapabilityOperation.LlmSubmit:case CapabilityOperation.LlmCancel:case CapabilityOperation.SoundPcm:Need(request>0&&(!error||toWorker),"capability-request-role");break;
    case CapabilityOperation.InputSnapshot:case CapabilityOperation.AudioSnapshot:case CapabilityOperation.ViewSnapshot:Need(toWorker&&!error&&request==0,"capability-push-role");break;
    default:throw new InvalidDataException("capability-operation");
   }
  }
  public static void Encode(byte[] header,WorkerIdentity identity,CapabilityOperation operation,bool toWorker,bool error,int request,long sequence,int payloadBytes){
   Need(header!=null&&header.Length==HeaderBytes&&identity!=null,"capability-header");Need(sequence>0,"capability-sequence");Shape(operation,toWorker,error,request,payloadBytes);
   Put(header,0,Magic,4);Put(header,4,1,2);Put(header,6,(ushort)operation,2);Put(header,8,(uint)payloadBytes,4);Buffer.BlockCopy(identity.AttemptBytes,0,header,12,16);Buffer.BlockCopy(identity.InstanceBytes,0,header,28,16);Put(header,44,identity.Generation,4);Put(header,48,(ulong)sequence,8);Put(header,56,(uint)request,4);Put(header,60,(toWorker?1UL:0UL)|(error?2UL:0UL),4);
  }
  public static CapabilityHeader Validate(byte[] header,WorkerIdentity expected,bool toWorker,long previousSequence){
   Need(header!=null&&header.Length==HeaderBytes&&expected!=null,"capability-header");Need(previousSequence>=0,"capability-previous-sequence");Need(U32(header,0)==Magic&&header[4]==1&&header[5]==0,"capability-version");
   uint flags=U32(header,60);Need(flags==(toWorker?1U:0U)||toWorker&&flags==3,"capability-direction-or-flags");
   Need(Match(header,12,expected.AttemptBytes)&&Match(header,28,expected.InstanceBytes)&&U32(header,44)==expected.Generation,"capability-session");
   ulong sequence=U64(header,48);Need(sequence>0&&sequence<=long.MaxValue&&sequence>(ulong)previousSequence,"capability-sequence");
   uint payload=U32(header,8),request=U32(header,56);Need(payload<=MaxPayloadBytes&&request<=int.MaxValue,"capability-length-or-request");
   var operation=(CapabilityOperation)(header[6]|header[7]<<8);bool error=(flags&2)!=0;Shape(operation,toWorker,error,(int)request,(int)payload);return new CapabilityHeader(operation,(long)sequence,(int)request,(int)payload,error);
  }
 }
}

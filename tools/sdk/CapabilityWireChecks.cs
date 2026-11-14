using System;using System.IO;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class CapabilityWireChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static void Reject(Action body){bool failed=false;try{body();}catch(InvalidDataException){failed=true;}Need(failed,"invalid-envelope-accepted");}
 static int Main(){try{
  var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),7);var header=new byte[CapabilityWire.HeaderBytes];
  CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,false,false,123,1,256);var value=CapabilityWire.Validate(header,identity,false,0);
  Need(value.RequestId==123&&value.Sequence==1&&value.PayloadBytes==256&&value.Operation==CapabilityOperation.StorageRead&&!value.Error,"roundtrip");
  Reject(()=>CapabilityWire.Validate(header,identity,true,0));Reject(()=>CapabilityWire.Validate(header,identity,false,1));
  Reject(()=>CapabilityWire.Validate(header,new WorkerIdentity(Guid.NewGuid(),identity.Instance,7),false,0));Reject(()=>CapabilityWire.Validate(header,new WorkerIdentity(identity.Attempt,Guid.NewGuid(),7),false,0));Reject(()=>CapabilityWire.Validate(header,new WorkerIdentity(identity.Attempt,identity.Instance,8),false,0));
  foreach(int offset in new[]{0,4,6,10,44,60}){byte[] bad=(byte[])header.Clone();bad[offset]^=128;Reject(()=>CapabilityWire.Validate(bad,identity,false,0));}
  foreach(int length in new[]{0,-1,65537,int.MaxValue})Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,false,false,1,1,length));
  Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,false,true,1,1,1));Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,false,false,0,1,1));
  Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,false,false,0,1,1));Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.StorageRead,false,false,1,0,1));
  CapabilityWire.Encode(header,identity,CapabilityOperation.LlmSubmit,true,true,3,long.MaxValue,65536);Need(CapabilityWire.Validate(header,identity,true,long.MaxValue-1).Error,"error-reply");Reject(()=>CapabilityWire.Validate(header,identity,true,long.MaxValue));
  CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,true,false,0,1,1);Need(CapabilityWire.Validate(header,identity,true,0).RequestId==0,"push");Reject(()=>CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,true,true,0,1,1));
  Console.WriteLine("PASS bounded capability envelopes, roles, replay, generation and terminal sequence");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

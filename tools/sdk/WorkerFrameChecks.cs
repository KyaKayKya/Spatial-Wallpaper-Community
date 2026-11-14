using System;using System.IO;using SpatialWallpaper.Sdk.Hosting;
static class WorkerFrameChecks {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("frame-not-rejected");}
 static int Main(){try{
  var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var header=new byte[WorkerFrameCodec.HeaderBytes];
  Assert(typeof(WorkerFrameCodec).Assembly.GetType("SpatialWallpaper.Sdk.Hosting.WorkerProjectionCodec")!=null,"trusted-projection-control-unavailable");
  WorkerFrameCodec.Encode(header,identity,640,360,1);
  Assert(WorkerFrameCodec.Validate(header,identity,640,360,0)==1,"valid-frame-rejected");
  Reject(()=>WorkerFrameCodec.Validate(header,identity,640,360,1));
  Reject(()=>WorkerFrameCodec.Validate(header,new WorkerIdentity(Guid.NewGuid(),identity.Instance,1),640,360,0));
  Reject(()=>WorkerFrameCodec.Validate(header,new WorkerIdentity(identity.Attempt,identity.Instance,2),640,360,0));
  Reject(()=>WorkerFrameCodec.Validate(header,new WorkerIdentity(identity.Attempt,Guid.NewGuid(),1),640,360,0));
  Reject(()=>WorkerFrameCodec.Validate(header,identity,1280,720,0));
  for(int i=0;i<header.Length;i++){
   var bad=(byte[])header.Clone();bad[i]^=0x80;
   // Sequence bytes may represent another valid strictly increasing sequence.
   if(i<56)Reject(()=>WorkerFrameCodec.Validate(bad,identity,640,360,0));
  }
  Reject(()=>WorkerFrameCodec.Encode(new byte[WorkerFrameCodec.HeaderBytes],identity,int.MaxValue,2,1));
  Reject(()=>WorkerFrameCodec.Encode(new byte[WorkerFrameCodec.HeaderBytes],identity,8192,8192,1));
  Reject(()=>WorkerFrameCodec.Validate(new byte[WorkerFrameCodec.HeaderBytes-1],identity,640,360,0));
  Reject(()=>WorkerFrameCodec.Validate(new byte[WorkerFrameCodec.HeaderBytes+1],identity,640,360,0));
  var configuration=new ProjectionConfiguration(7,640,360,.53f,.298125f,.1f,.05f,-.6f);
  var control=WorkerProjectionCodec.Encode(identity,configuration);var decoded=WorkerProjectionCodec.Decode(control,identity,6);
  Assert(decoded.Revision==7&&decoded.Width==640&&decoded.Height==360&&decoded.EyeX==.1f&&decoded.EyeZ==-.6f&&!decoded.PreserveWidth,"projection-control-values");
  Reject(()=>WorkerProjectionCodec.Decode(control,identity,7));Reject(()=>WorkerProjectionCodec.Decode(control,new WorkerIdentity(Guid.NewGuid(),identity.Instance,1),0));
  Reject(()=>WorkerProjectionCodec.Decode(control,new WorkerIdentity(identity.Attempt,identity.Instance,2),0));
  var reserved=(byte[])control.Clone();reserved[95]=1;Reject(()=>WorkerProjectionCodec.Decode(reserved,identity,0));
  var invalidFit=(byte[])control.Clone();invalidFit[80]=2;Reject(()=>WorkerProjectionCodec.Decode(invalidFit,identity,0));
  var nan=(byte[])control.Clone();nan[68]=0;nan[69]=0;nan[70]=192;nan[71]=127;Reject(()=>WorkerProjectionCodec.Decode(nan,identity,0));
  bool invalid=false;try{new ProjectionConfiguration(1,640,360,.53f,.298125f,0,0,0);}catch(ArgumentException){invalid=true;}Assert(invalid,"eye-behind-display-required");
  invalid=false;try{new ProjectionConfiguration(1,640,360,.53f,.4f,0,0,-.6f);}catch(ArgumentException){invalid=true;}Assert(invalid,"physical-and-pixel-aspect-must-match");
  WorkerFrameCodec.Encode(header,identity,640,360,8,7);long observedRevision;Assert(WorkerFrameCodec.Validate(header,identity,640,360,0,out observedRevision)==8&&observedRevision==7,"frame-projection-revision-binding");
  header[4]=1;Reject(()=>WorkerFrameCodec.Validate(header,identity,640,360,0));
  Assert(WorkerFrameCodec.PayloadBytes(3840,2160)==3840*2160*4,"4K-payload-size");
  byte[] rows={1,2,3,4,5,6,7,8,9,10,11,12};
  WorkerFrameCodec.FlipVerticalRows(rows,1,3,new byte[4]);
  Assert(rows[0]==9&&rows[4]==5&&rows[8]==1,"odd-height-row-normalization");
  var unchanged=(byte[])rows.Clone();Reject(()=>WorkerFrameCodec.FlipVerticalRows(rows,1,3,new byte[3]));
  for(int i=0;i<rows.Length;i++)Assert(rows[i]==unchanged[i],"invalid-row-buffer-mutated-frame");
  Reject(()=>WorkerFrameCodec.FlipVerticalRows(rows,1,3,rows));
  FrameMailboxChecks.Run();
  var cadence=new FrameCadence(60,1000000);Assert(cadence.TryBegin(0),"first-frame-cadence");
  Assert(!cadence.TryBegin(16666)&&cadence.TryBegin(16667),"monotonic-frame-rate-bound");
  Assert(cadence.TryBegin(1000000)&&!cadence.TryBegin(1000000)&&!cadence.TryBegin(1000001)&&cadence.TryBegin(1016667),"no-unbounded-catch-up-after-stall");
  Assert(cadence.TryBegin(long.MaxValue)&&!cadence.TryBegin(long.MaxValue),"cadence-clock-overflow");
  bool backwards=false;try{cadence.TryBegin(1);}catch(InvalidOperationException){backwards=true;}Assert(backwards,"backwards-cadence-clock");
  var regular=new FrameCadence(60,1000000);int delivered=0;for(int i=0;i<600;i++)if(regular.TryBegin(i*16666L))delivered++;
  Assert(delivered>=598&&delivered<=600,"60Hz-clock-must-not-collapse-to-half-rate:"+delivered);
  Console.WriteLine("PASS exact identity/generation/sequence, header, dimensions and payload preflight");return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}

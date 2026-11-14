using System;
using System.IO;
using SpatialWallpaper.Sdk.Capabilities;
static class InputSnapshotChecks {
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static byte[] Frame(ulong epoch,long time,uint held,uint primary,uint space,bool focus=true){
  var down=new uint[7];down[0]=primary;down[2]=space;
  return InputSnapshotCodec.Encode(epoch,time,focus,held,(ushort)(focus?100:0),(ushort)(focus?200:0),0,down,new uint[7]);
 }
 static int Main(){try{
  var input=new InputFrames();
  input.Advance(Frame(1,100,0,2,0),100,true);
  Need(input.GetButtonDown("Primary")&&!input.GetButton("Primary"),"coalesced-click-lost");
  Need(input.GetButtonDown("Primary"),"query-consumed-edge");
  input.Advance(null,101,true);Need(!input.GetButtonDown("Primary"),"edge-repeated-next-frame");
  input.Advance(Frame(1,102,4,2,1),102,true);Need(input.GetButtonDown("Space")&&input.GetButton("Space"),"space-missing");
  input.Advance(Frame(2,103,0,0,0,false),103,true);Need(!input.Focused&&!input.GetButton("Space"),"revocation-retained-key");
  input.Advance(Frame(1,104,4,2,2),104,true);Need(!input.Focused,"old-epoch-revived");
  input.Advance(Frame(3,105,1,1,0),105,true);Need(input.GetButtonDown("Primary"),"new-focus-click-lost");
  input.Advance(null,356,true);Need(!input.Focused,"silence-kept-focus");
  input.Advance(Frame(3,357,4,1,1),357,true);Need(!input.Focused,"timed-out-epoch-revived");
  input.Advance(Frame(4,358,1,1,0),358,true);Need(input.Focused,"fresh-epoch-failed");
  input.Advance(null,359,false);Need(!input.Focused,"disconnect-kept-focus");
  var stale=new InputFrames();stale.Advance(Frame(1,1,1,1,0),300,true);Need(!stale.Focused,"late-pipe-data-freshened");
  stale.Advance(Frame(2,500,1,1,0),400,true);Need(!stale.Focused,"future-timestamp-accepted");
  var malformed=new InputFrames();bool rejected=false;
  try{malformed.Advance(new byte[95],100,true);}catch(InvalidDataException){rejected=true;}
  Need(rejected&&!malformed.Focused,"bad-size-accepted");
  bool unknown=false;try{input.GetButton("Password");}catch(ArgumentException){unknown=true;}Need(unknown,"unbounded-button-name");
  var wheel=new InputFrames();
  wheel.Advance(InputSnapshotCodec.Encode(1,1,true,0,65535,0,120,new uint[7],new uint[7]),1,true);
  Need(wheel.Wheel==1&&wheel.X==1&&wheel.Y==0,"wheel-or-position-missing");
  Need(wheel.Wheel==1,"wheel-query-consumed-delta");
  wheel.Advance(InputSnapshotCodec.Encode(1,2,true,0,0,65535,-120,new uint[7],new uint[7]),2,true);
  Need(wheel.Wheel==-2&&wheel.X==0&&wheel.Y==1,"wheel-reversal-delta-wrong");
  wheel.Advance(null,3,true);Need(wheel.Wheel==0,"wheel-replayed-next-frame");
  wheel.Advance(InputSnapshotCodec.Encode(2,4,false,0,0,0,0,new uint[7],new uint[7]),4,true);
  Need(wheel.Wheel==0&&wheel.X==0&&wheel.Y==0,"revocation-retained-pointer");
  wheel.Advance(InputSnapshotCodec.Encode(3,5,true,0,0,0,120,new uint[7],new uint[7]),5,true);
  Need(wheel.Wheel==1,"new-epoch-reused-wheel-baseline");
  wheel.Advance(InputSnapshotCodec.Encode(3,5,true,0,0,0,120,new uint[7],new uint[7]),5,true);
  Need(wheel.Wheel==0,"duplicate-snapshot-replayed-wheel");
  wheel.Advance(InputSnapshotCodec.Encode(3,6,true,0,0,0,480,new uint[7],new uint[7]),6,true);
  Need(wheel.Wheel==3,"coalesced-wheel-delta-lost");
  foreach(long boundary in new[]{-12000000L,12000000L}){
   var bounded=new InputFrames();bounded.Advance(InputSnapshotCodec.Encode(1,1,true,0,0,0,boundary,new uint[7],new uint[7]),1,true);
   Need(bounded.Wheel==boundary/120f,"wheel-boundary-rejected");
  }
  foreach(long invalidWheel in new[]{-12000001L,12000001L,long.MinValue,long.MaxValue}){
   var bounded=new InputFrames();bounded.Advance(InputSnapshotCodec.Encode(1,1,true,4,65535,65535,120,new uint[7],new uint[7]),1,true);
   bool wheelRejected=false;try{bounded.Advance(InputSnapshotCodec.Encode(1,2,true,4,65535,65535,invalidWheel==12000001L?12000121L:invalidWheel,new uint[7],new uint[7]),2,true);}catch(InvalidDataException){wheelRejected=true;}
   Need(wheelRejected&&!bounded.Focused&&!bounded.GetButton("Space")&&bounded.Wheel==0&&bounded.X==0&&bounded.Y==0,"invalid-wheel-did-not-clear");
  }
  Console.WriteLine("PASS input snapshot coalescing, frame-stable edges, epochs, stale/future data, disconnect and exact bounds");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

using System;using System.IO;using SpatialWallpaper.Sdk.Capabilities;
static class AudioSnapshotChecks {
 static void Need(bool b,string m){if(!b)throw new Exception(m);}
 static void Put(byte[] b,int at,ulong n,int count){for(int i=0;i<count;i++)b[at+i]=(byte)(n>>(8*i));}
 static byte[] Frame(ulong seq,ulong captured,uint state=1){var b=new byte[40];Put(b,0,0x46415753,4);Put(b,4,2,4);Put(b,8,seq,8);Put(b,16,123,8);Put(b,24,captured,8);Put(b,32,state,4);if(state==1&&captured>0)b[36]=15;return b;}
 static void Invalid(AudioFrames f,byte[] b,ulong now){bool threw=false;try{f.Advance(b,now,true);}catch(InvalidDataException){threw=true;}Need(threw&&f.GetBand(0)==0,"invalid did not clear and reject");}
 static int Main(){try{
  var f=new AudioFrames();Need(f.GetBand(0)==0&&f.State==AudioFeatureState.Unavailable,"initial state");
  f.Advance(Frame(1,10000000),10000000,true);Need(f.GetBand(0)==1&&f.GetBand(1)==0,"normalized bands");
  f.Advance(null,13999999,true);Need(f.GetBand(0)==1,"early expiry");
  f.Advance(null,14000001,true);Need(f.GetBand(0)==0,"stale retained");
  f.Advance(Frame(1,14000001),14000001,true);Need(f.GetBand(0)==0,"duplicate revision revived");
  f.Advance(Frame(2,14000001),14000001,true);Need(f.GetBand(0)==1,"fresh revision rejected");
  f.Advance(null,14000002,false);Need(f.State==AudioFeatureState.Unavailable&&f.GetBand(0)==0,"disconnect retained");
  f.Advance(Frame(2,14000002),14000002,true);Need(f.GetBand(0)==0,"disconnect replay revived");
  f.Advance(Frame(3,10000000),14000003,true);Need(f.GetBand(0)==0,"queue renewed stale data");
  Invalid(f,Frame(4,15000000),14000003);
  var bad=Frame(5,14000003);bad[39]=16;Invalid(f,bad,14000003);
  bad=Frame(6,14000003);bad[32]=2;Invalid(f,bad,14000003);
  Invalid(f,new byte[41],14000003);
  f.Advance(Frame(7,14000003),14000003,true);f.Advance(Frame(8,0,2),14000004,true);Need(f.State==AudioFeatureState.SourceEnded&&f.GetBand(0)==0,"terminal retained");
  f.Advance(Frame(9,14000004),14000004,true);f.Advance(null,14000003,true);Need(f.GetBand(0)==0,"clock regression retained");
  bool bounds=false;try{f.GetBand(4);}catch(ArgumentOutOfRangeException){bounds=true;}Need(bounds,"band bounds");
  Console.WriteLine("PASS audio snapshot normalization, original capture expiry, duplicate/replay, disconnect, malformed frames, clock regression and terminal state");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

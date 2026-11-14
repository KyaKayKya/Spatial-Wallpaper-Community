using System;
using System.IO;

namespace SpatialWallpaper.Sdk.Capabilities {
 // Trusted authorization metadata only. Epoch zero is revoked; no raw input,
 // button state or author-controlled values can be carried by this record.
 public readonly struct InputAuthority {
  public readonly ulong Epoch,Time;
  public InputAuthority(ulong epoch,ulong time){Epoch=epoch;Time=time;}
 }
 // Native-only companion metadata. The existing 96-byte snapshot and legacy
 // author input API stay unchanged; these events preserve accepted source order.
 public sealed class OrderedInputEvent {
  public readonly long Sequence,Time;public readonly ulong Epoch;public readonly uint Held,Pressed,Released;public readonly int Wheel;
  public OrderedInputEvent(long sequence,long time,ulong epoch,uint held,uint pressed,uint released,int wheel){
   if(sequence<=0||time<0||epoch==0||((held|pressed|released)&~127u)!=0||(pressed&released)!=0||(pressed&~held)!=0||(released&held)!=0||wheel < -1200||wheel>1200||((pressed|released)==0&&wheel==0))throw new ArgumentException("ordered-input-event");
   Sequence=sequence;Time=time;Epoch=epoch;Held=held;Pressed=pressed;Released=released;Wheel=wheel;
  }
 }
 public sealed class OrderedInputBatch {
  public const int MaxEvents=256;
  public readonly byte[] Snapshot;public readonly OrderedInputEvent[] Events;public readonly long Cursor;
  public OrderedInputBatch(byte[] snapshot,OrderedInputEvent[] events,long cursor){
   InputSnapshotCodec.RequireShape(snapshot);if(events==null||events.Length>MaxEvents||cursor<0)throw new ArgumentException("ordered-input-batch");
   ulong epoch=InputSnapshotCodec.U64(snapshot,8),stamp=InputSnapshotCodec.U64(snapshot,16);long previous=0;
   foreach(var value in events){if(value==null||value.Sequence<=previous||value.Sequence>cursor||value.Epoch!=epoch||(ulong)value.Time>stamp||InputSnapshotCodec.U32(snapshot,4)==0)throw new ArgumentException("ordered-input-batch-event");previous=value.Sequence;}
   Snapshot=(byte[])snapshot.Clone();Events=(OrderedInputEvent[])events.Clone();Cursor=cursor;
  }
 }
 // Fixed-size little-endian payload inside the authenticated capability envelope.
 // Counters preserve short clicks when the single-slot push mailbox coalesces frames.
 public static class InputSnapshotCodec {
  public const int Bytes=96;
  internal static uint U32(byte[] b,int at)=>(uint)(b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24);
  internal static ulong U64(byte[] b,int at)=>U32(b,at)|((ulong)U32(b,at+4)<<32);
  static void Put(byte[] b,int at,uint value){for(int i=0;i<4;i++)b[at+i]=(byte)(value>>(i*8));}
  static void Put64(byte[] b,int at,ulong value){Put(b,at,(uint)value);Put(b,at+4,(uint)(value>>32));}
  public static void RequireShape(byte[] bytes){
   if(bytes==null||bytes.Length!=Bytes||U32(bytes,0)!=1||U32(bytes,4)>1||U64(bytes,16)>long.MaxValue)throw new InvalidDataException("input-snapshot-shape");
   uint position=U32(bytes,28);var pressed=new uint[7];var released=new uint[7];
   for(int i=0;i<7;i++){pressed[i]=U32(bytes,40+i*4);released[i]=U32(bytes,68+i*4);}
   // Reuse the actual encoder's field rules; freshness/epoch ordering remains
   // InputFrames' responsibility at the point of author execution.
   Encode(U64(bytes,8),(long)U64(bytes,16),U32(bytes,4)==1,U32(bytes,24),(ushort)position,(ushort)(position>>16),unchecked((long)U64(bytes,32)),pressed,released);
  }
  public static byte[] Encode(ulong epoch,long time,bool focused,uint held,ushort x,ushort y,long wheel,uint[] pressed,uint[] released){
   if(epoch==0||time<0||(held&~127u)!=0||pressed==null||released==null||pressed.Length!=7||released.Length!=7)throw new ArgumentException("input-snapshot-fields");
   if(!focused&&(held!=0||x!=0||y!=0||wheel!=0))throw new ArgumentException("input-unfocused-state");
   var bytes=new byte[Bytes];Put(bytes,0,1);Put(bytes,4,focused?1u:0u);Put64(bytes,8,epoch);Put64(bytes,16,(ulong)time);Put(bytes,24,held);Put(bytes,28,(uint)x|((uint)y<<16));Put64(bytes,32,unchecked((ulong)wheel));
   for(int i=0;i<7;i++){if(!focused&&(pressed[i]!=0||released[i]!=0))throw new ArgumentException("input-unfocused-edge");Put(bytes,40+i*4,pressed[i]);Put(bytes,68+i*4,released[i]);}return bytes;
  }
 }
 // Updated once before each author frame. Queries never consume edges.
 // now and wire time use the SAME monotonic machine clock (GetTickCount64 on Windows).
 public sealed class InputFrames {
  readonly uint[] downs=new uint[7],ups=new uint[7];
  ulong epoch,blockedThrough;
  long lastTime,clock,wheelTotal;
  uint held,pressed,released;
  public bool Focused{get;private set;}
  public float X{get;private set;}public float Y{get;private set;}
  public float Wheel{get;private set;}
  void Clear(bool block){if(block)blockedThrough=Math.Max(blockedThrough,epoch);Focused=false;held=pressed=released=0;X=Y=Wheel=0;}
  static int Button(string name){switch(name){case "Primary":return 0;case "Secondary":return 1;case "Space":return 2;case "Left":return 3;case "Right":return 4;case "Up":return 5;case "Down":return 6;default:throw new ArgumentException("input-button-name");}}
  public bool GetButton(string name)=>(held&(1u<<Button(name)))!=0;
  public bool GetButtonDown(string name)=>(pressed&(1u<<Button(name)))!=0;
  public bool GetButtonUp(string name)=>(released&(1u<<Button(name)))!=0;
  public void Advance(byte[] bytes,long now,bool connected){
   pressed=released=0;Wheel=0;
   if(now<clock||!connected){Clear(true);return;}clock=now;
   if(Focused&&now-lastTime>250)Clear(true);
   if(bytes==null)return;
   try{
    if(bytes.Length!=InputSnapshotCodec.Bytes||InputSnapshotCodec.U32(bytes,0)!=1||InputSnapshotCodec.U32(bytes,4)>1)throw new InvalidDataException("input-snapshot-shape");
    ulong incoming=InputSnapshotCodec.U64(bytes,8),stamp=InputSnapshotCodec.U64(bytes,16);
    uint nextHeld=InputSnapshotCodec.U32(bytes,24),position=InputSnapshotCodec.U32(bytes,28);
    bool focus=InputSnapshotCodec.U32(bytes,4)==1;
    if(incoming==0||stamp>long.MaxValue||(nextHeld&~127u)!=0)throw new InvalidDataException("input-snapshot-fields");
    if(!focus&&(nextHeld!=0||position!=0||InputSnapshotCodec.U64(bytes,32)!=0))throw new InvalidDataException("input-unfocused-state");
    if(incoming<=blockedThrough||incoming<epoch||stamp>(ulong)now||(ulong)now-stamp>250||(incoming==epoch&&(long)stamp<lastTime)){Clear(true);blockedThrough=Math.Max(blockedThrough,incoming);return;}
    bool fresh=incoming>epoch;
    long nextWheel=unchecked((long)InputSnapshotCodec.U64(bytes,32));
    long wheelDelta;try{wheelDelta=checked(nextWheel-(fresh?0:wheelTotal));}catch(OverflowException){throw new InvalidDataException("input-wheel-overflow");}
    if(wheelDelta < -12000000 || wheelDelta > 12000000)throw new InvalidDataException("input-wheel-delta-bound");
    for(int i=0;i<7;i++){
     uint down=InputSnapshotCodec.U32(bytes,40+i*4),up=InputSnapshotCodec.U32(bytes,68+i*4);
     if(!focus&&(down!=0||up!=0))throw new InvalidDataException("input-unfocused-edge");
     if(!fresh&&(down<downs[i]||up<ups[i]))throw new InvalidDataException("input-counter-regressed");
    }
    for(int i=0;i<7;i++){
     uint down=InputSnapshotCodec.U32(bytes,40+i*4),up=InputSnapshotCodec.U32(bytes,68+i*4);
     if(down>(fresh?0:downs[i]))pressed|=1u<<i;
     if(up>(fresh?0:ups[i]))released|=1u<<i;
     downs[i]=down;ups[i]=up;
    }
    epoch=incoming;lastTime=(long)stamp;Focused=focus;held=nextHeld;X=(position&65535)/65535f;Y=(position>>16)/65535f;wheelTotal=nextWheel;Wheel=wheelDelta/120f;
    if(!focus)Clear(true);
   }catch{Clear(true);throw;}
  }
 }
}

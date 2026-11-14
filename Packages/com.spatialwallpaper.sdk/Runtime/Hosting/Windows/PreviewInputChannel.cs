using System;using System.IO;using System.Diagnostics;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Supervision.Broker {
 // Dedicated trusted manager connection, authenticated and identity-bound by
 // Host before Serve. Never expose this stream or its credentials to a Worker.
 // One request in flight, no queue; original event time and epoch are preserved.
 public sealed class PreviewInputChannel:IPreviewInputTarget,IPreviewInputKeepAlive,IDisposable {
  const int Size=56,DeadlineMs=200;readonly Stream stream;ulong epoch;bool closed;
  public PreviewInputChannel(Stream authenticatedStream){stream=authenticatedStream??throw new ArgumentNullException(nameof(authenticatedStream));try{epoch=ReadEpoch(stream);}catch{Dispose();throw;}}
  public ulong CaptureEpoch{get{if(closed)throw new ObjectDisposedException(nameof(PreviewInputChannel));return epoch;}}
  static long Deadline()=>Stopwatch.GetTimestamp()+DeadlineMs*Stopwatch.Frequency/1000;
  static int Remaining(long deadline){long left=(deadline-Stopwatch.GetTimestamp())*1000/Stopwatch.Frequency;if(left<=0)throw new TimeoutException("preview-input-channel-deadline");return (int)left;}
  static void Read(Stream stream,byte[] bytes){long deadline=Deadline();int at=0;while(at<bytes.Length){var pending=stream.ReadAsync(bytes,at,bytes.Length-at);if(!pending.Wait(Remaining(deadline)))throw new TimeoutException("preview-input-channel-read");int count=pending.GetAwaiter().GetResult();if(count==0)throw new EndOfStreamException();at+=count;}}
  static void Write(Stream stream,byte[] bytes){var pending=stream.WriteAsync(bytes,0,bytes.Length);if(!pending.Wait(DeadlineMs))throw new TimeoutException("preview-input-channel-write");pending.GetAwaiter().GetResult();}
  static byte[] Epoch(ulong value){var bytes=new byte[16];Buffer.BlockCopy(BitConverter.GetBytes(0x31495053u),0,bytes,0,4);Buffer.BlockCopy(BitConverter.GetBytes(1u),0,bytes,4,4);Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,8,8);return bytes;}
  static ulong ReadEpoch(Stream stream){var bytes=new byte[16];Read(stream,bytes);ulong value=BitConverter.ToUInt64(bytes,8);if(BitConverter.ToUInt32(bytes,0)!=0x31495053u||BitConverter.ToUInt32(bytes,4)!=1||value==0)throw new InvalidDataException("preview-input-channel-epoch");return value;}
  void Exchange(byte[] packet){if(closed)throw new ObjectDisposedException(nameof(PreviewInputChannel));try{Write(stream,packet);epoch=ReadEpoch(stream);}catch{Dispose();throw;}}
  public void Observe(InputObservation value){
   var bytes=new byte[Size];using(var writer=new BinaryWriter(new MemoryStream(bytes))){
    writer.Write(1u);writer.Write(1u);writer.Write(value.Sequence);writer.Write(value.Time);writer.Write(value.CaptureEpoch);
    uint flags=(value.Target?1u:0)|(value.Foreground?2u:0)|(value.ShellFocus?4u:0)|(value.Session?8u:0)|(value.Adapter?16u:0)|(value.Modifier?32u:0)|(value.PrimaryDown?64u:0)|(value.Heartbeat?128u:0);
    writer.Write(flags);writer.Write(value.Held);writer.Write(value.X);writer.Write(value.Y);writer.Write(value.Wheel);writer.Write(0u);
   }Exchange(bytes);
  }
  public void Revoke(){if(closed)return;var bytes=new byte[Size];bytes[0]=1;bytes[4]=2;Exchange(bytes);}
  public void KeepAlive(){var bytes=new byte[Size];bytes[0]=1;bytes[4]=3;Exchange(bytes);}
  public static void Serve(Stream authenticatedStream,InputTarget target){
   if(authenticatedStream==null||target==null)throw new ArgumentNullException();target.Owner.AttachSource(target);
   try{
    Write(authenticatedStream,Epoch(target.CaptureEpoch));long sequence=0;
    while(!target.Closed){
     var bytes=new byte[Size];Read(authenticatedStream,bytes);
     using(var reader=new BinaryReader(new MemoryStream(bytes))){
      if(reader.ReadUInt32()!=1)throw new InvalidDataException("preview-input-channel-version");uint operation=reader.ReadUInt32();
      if(operation==2||operation==3){for(int i=8;i<Size;i++)if(bytes[i]!=0)throw new InvalidDataException("preview-input-control-shape");if(operation==2)target.Revoke();}
      else if(operation==1){
       long next=reader.ReadInt64(),time=reader.ReadInt64();ulong epoch=reader.ReadUInt64();uint flags=reader.ReadUInt32(),held=reader.ReadUInt32();float x=reader.ReadSingle(),y=reader.ReadSingle();int wheel=reader.ReadInt32();
       if(next<=sequence||next<=0||(flags&~255u)!=0||reader.ReadUInt32()!=0)throw new InvalidDataException("preview-input-channel-fields");sequence=next;
       target.Observe(new InputObservation(next,time,(flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0,(flags&16)!=0,(flags&32)!=0,(flags&64)!=0,held,x,y,wheel,epoch,(flags&128)!=0));
      }else throw new InvalidDataException("preview-input-channel-operation");
     }
     Write(authenticatedStream,Epoch(target.CaptureEpoch));
    }
   }finally{target.Owner.DetachSource(target);}
  }
  public void Dispose(){if(closed)return;closed=true;stream.Dispose();}
 }
}

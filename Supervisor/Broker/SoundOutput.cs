using System;using System.Runtime.InteropServices;using System.Threading;using SpatialWallpaper.Sdk;
namespace SpatialWallpaper.Supervision.Broker {
 // Trusted local PCM output. No paths, codecs, devices or volume controls are
 // accepted from the sandbox. Fixed buffers; the owning session closes it.
 public sealed class SoundOutput:IDisposable {
  [StructLayout(LayoutKind.Sequential,Pack=2)]struct Format {public ushort tag,channels;public uint rate,average;public ushort align,bits,extra;}
  [StructLayout(LayoutKind.Sequential)]struct Header {public IntPtr data;public uint length,recorded;public UIntPtr user;public uint flags,loops;public IntPtr next;public UIntPtr reserved;}
  [DllImport("winmm.dll")]static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
  [DllImport("winmm.dll")]static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern uint waveOutReset(IntPtr handle);
  [DllImport("winmm.dll")]static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern uint waveOutClose(IntPtr handle);
  readonly object gate=new object();readonly byte[][] queue=new byte[8][];readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Thread thread;
  int head,count;bool closed,disposed,paused,reset;float music,effects;string error,cleanupError;long submitted;
  public string Error{get{lock(gate)return error;}}public long Submitted=>Interlocked.Read(ref submitted);
  public SoundOutput(){for(int i=0;i<queue.Length;i++)queue[i]=new byte[SoundFormat.PacketBytes];thread=new Thread(Run){IsBackground=true,Name="SpatialWallpaper sound output"};thread.Start();}
  static void Gain(float value){if(float.IsNaN(value)||value<0||value>1)throw new ArgumentException("sound-mix-gain");}
  public void Configure(float musicVolume,float effectsVolume,bool pause=false){Gain(musicVolume);Gain(effectsVolume);lock(gate){music=musicVolume;effects=effectsVolume;if(paused!=pause){paused=pause;reset=true;count=0;}}wake.Set();}
  public bool Submit(byte[] packet,int length){if(packet==null||length!=SoundFormat.PacketBytes||packet.Length<length)throw new ArgumentException("sound-packet-size");lock(gate){if(closed||paused||error!=null||count==queue.Length)return false;Buffer.BlockCopy(packet,0,queue[(head+count)%queue.Length],0,length);count++;}wake.Set();return true;}
  public static void Mix(byte[] packet,byte[] output,float music,float effects,bool pause){
   Gain(music);Gain(effects);if(packet.Length!=SoundFormat.PacketBytes||output.Length!=SoundFormat.Frames*2)throw new ArgumentException("sound-packet-size");
   for(int i=0;i<SoundFormat.Frames;i++)SoundFormat.Write(output,i*2,pause?0:(int)(SoundFormat.Read(packet,i*2)*music+SoundFormat.Read(packet,SoundFormat.Frames*2+i*2)*effects));
  }
  void Run(){
   IntPtr handle=IntPtr.Zero;var headers=new IntPtr[4];var buffers=new IntPtr[4];var busy=new bool[4];var prepared=new bool[4];uint size=(uint)Marshal.SizeOf(typeof(Header));var packet=new byte[SoundFormat.PacketBytes];var output=new byte[SoundFormat.Frames*2];
   try{while(true){
    bool flush;lock(gate){if(closed)break;flush=reset;reset=false;}if(flush&&handle!=IntPtr.Zero){Check(waveOutReset(handle));Array.Clear(busy,0,busy.Length);}
    int slot=-1;for(int i=0;i<4;i++){if(busy[i]&&(((Header)Marshal.PtrToStructure(headers[i],typeof(Header))).flags&1)!=0)busy[i]=false;if(!busy[i]&&slot<0)slot=i;}
    if(slot<0){wake.WaitOne(2);continue;}
    float mg,fg;lock(gate){if(count==0){mg=fg=0;}else{Buffer.BlockCopy(queue[head],0,packet,0,packet.Length);head=(head+1)%queue.Length;count--;mg=music;fg=effects;goto Ready;}}wake.WaitOne(5);continue;
    Ready:
    if(handle==IntPtr.Zero){var format=new Format{tag=1,channels=1,rate=SoundFormat.Rate,average=SoundFormat.Rate*2,align=2,bits=16};Check(waveOutOpen(out handle,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,0));
     for(int i=0;i<4;i++){buffers[i]=Marshal.AllocHGlobal(output.Length);headers[i]=Marshal.AllocHGlobal((int)size);Marshal.StructureToPtr(new Header{data=buffers[i],length=(uint)output.Length},headers[i],false);Check(waveOutPrepareHeader(handle,headers[i],size));prepared[i]=true;}
    }
    Mix(packet,output,mg,fg,false);Marshal.Copy(output,0,buffers[slot],output.Length);Check(waveOutWrite(handle,headers[slot],size));busy[slot]=true;Interlocked.Increment(ref submitted);
   }}catch(Exception failure){lock(gate){error=failure.Message;count=0;}}
   finally{if(handle!=IntPtr.Zero)CleanupResult(waveOutReset(handle));for(int i=0;i<4;i++){
    // A driver can still own a prepared buffer after reset fails. Preserve that
    // bounded allocation until process teardown rather than freeing live memory.
    bool released=!prepared[i];if(prepared[i]){uint code=waveOutUnprepareHeader(handle,headers[i],size);CleanupResult(code);released=code==0;}
    if(released){if(headers[i]!=IntPtr.Zero)Marshal.FreeHGlobal(headers[i]);if(buffers[i]!=IntPtr.Zero)Marshal.FreeHGlobal(buffers[i]);}
   }if(handle!=IntPtr.Zero)CleanupResult(waveOutClose(handle));}
  }
  void CleanupResult(uint code){if(code!=0&&cleanupError==null)cleanupError="sound-output-cleanup-failed:"+code;}
  static void Check(uint code){if(code!=0)throw new InvalidOperationException("sound-device-unavailable:"+code);}
  public void Close(){lock(gate){closed=true;count=0;}wake.Set();}
  public void Dispose(){if(disposed){if(cleanupError!=null)throw new InvalidOperationException(cleanupError);return;}Close();if(!thread.Join(1000))throw new InvalidOperationException("sound-output-still-alive");wake.Dispose();disposed=true;if(cleanupError!=null)throw new InvalidOperationException(cleanupError);}
 }
}

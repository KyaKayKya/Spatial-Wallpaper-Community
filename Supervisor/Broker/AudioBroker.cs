using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Runtime.InteropServices;using SpatialWallpaper.Supervision.Windows;
namespace SpatialWallpaper.Supervision.Broker {
 // One trusted selected source. No Worker-controlled PID, path, microphone, or fallback.
 public sealed class AudioBroker:IDisposable {
  readonly object gate=new object(),cleanupGate=new object();readonly TrustedAudioProcess child;readonly Thread reader;readonly Timer watchdog;bool disposed;
  byte[] latest;ulong revision=1;long lastRead;int closed,received;bool terminal;
  internal int SamplerProcessId=>child.ProcessId;internal long SamplerCreationFileTime=>child.CreationFileTime;
  [DllImport("kernel32.dll")]static extern ulong GetTickCount64();
  static ulong U64(byte[] b,int at)=>BitConverter.ToUInt64(b,at);static uint U32(byte[] b,int at)=>BitConverter.ToUInt32(b,at);
  static void Put(byte[] b,int at,ulong value,int size){for(int i=0;i<size;i++)b[at+i]=(byte)(value>>(8*i));}
  static long Now()=>checked((long)GetTickCount64());
  static ulong Qpc(){long ticks=Stopwatch.GetTimestamp(),frequency=Stopwatch.Frequency;return checked((ulong)(ticks/frequency)*10000000UL+(ulong)(ticks%frequency)*10000000UL/(ulong)frequency);}
  byte[] State(uint status){var value=new byte[40];Put(value,0,0x46415753,4);Put(value,4,2,4);Put(value,8,revision,8);Put(value,16,(ulong)Now(),8);Put(value,32,status,4);return value;}
  public AudioBroker(PrivateStation station,string executable,int sourceProcess,ulong creationTime,Action<Exception> cleanupFailure=null){
   latest=State(4);lastRead=Now();child=new TrustedAudioProcess(station,executable,sourceProcess,creationTime,cleanupFailure);bool readerStarted=false;
   try{reader=new Thread(Read){IsBackground=true,Name="SpatialWallpaper audio features"};reader.Start();readerStarted=true;watchdog=new Timer(Check,null,100,100);}
   catch{Interlocked.Exchange(ref closed,1);Exception failure=null;Action<Action> clean=operation=>{try{operation();}catch(Exception error){cleanupFailure?.Invoke(error);if(failure==null)failure=error;}};clean(child.Kill);clean(()=>{if(readerStarted&&!reader.Join(1000))throw new TimeoutException("audio-initialization-reader-cleanup-deadline");});clean(child.Dispose);if(failure!=null)throw failure;throw;}
  }
  public byte[] Snapshot(){lock(gate){var value=(byte[])latest.Clone();ulong captured=U64(value,24),now=Qpc();if(U32(value,32)==1&&(captured==0||captured>now||now-captured>4000000)){Array.Clear(value,24,8);Array.Clear(value,36,4);}return value;}}
  void Fail(){lock(gate){if(Volatile.Read(ref closed)!=0||terminal)return;terminal=true;revision=checked(revision+1);latest=State(5);child.Kill();}}
  void Check(object ignored){try{if(Volatile.Read(ref closed)==0&&Now()-Interlocked.Read(ref lastRead)>(Volatile.Read(ref received)==0?3500:1000))Fail();}catch{}}
  void Read(){try{
   ulong sequence=0,lastSent=0;var bytes=new byte[40];
   while(Volatile.Read(ref closed)==0){
    int at=0;while(at<40){int got=child.Read(bytes,at,40-at);if(got==0)throw new EndOfStreamException("audio-helper-eof");at+=got;}
    ulong incoming=U64(bytes,8),sent=U64(bytes,16),captured=U64(bytes,24),now=Qpc();uint status=U32(bytes,32);
    if(U32(bytes,0)!=0x46415753||U32(bytes,4)!=2||incoming!=sequence+1||status<1||status>5||sent>(ulong)Now()||sent<lastSent||status==1&&sequence>0&&sent-lastSent<200)throw new InvalidDataException("audio-frame-header");
    bool nonzero=false;for(int i=36;i<40;i++){if(bytes[i]>15)throw new InvalidDataException("audio-feature-value");nonzero|=bytes[i]!=0;}
    if(status!=1&&(captured!=0||nonzero)||nonzero&&captured==0||captured>now)throw new InvalidDataException("audio-frame-state");
    sequence=incoming;lastSent=sent;
    lock(gate){if(Volatile.Read(ref closed)!=0||terminal)break;revision=checked(revision+1);latest=(byte[])bytes.Clone();Put(latest,8,revision,8);terminal=status!=1;Interlocked.Exchange(ref lastRead,Now());Volatile.Write(ref received,1);if(terminal){child.Kill();break;}}
   }
  }catch{Fail();}}
  public void Dispose(){lock(cleanupGate){if(disposed)return;Interlocked.Exchange(ref closed,1);watchdog?.Dispose();lock(gate){revision=checked(revision+1);latest=State(4);child.Kill();}if(reader!=null&&!reader.Join(1500))throw new InvalidOperationException("audio-reader-stop-deadline");child.Dispose();disposed=true;}}
 }
}

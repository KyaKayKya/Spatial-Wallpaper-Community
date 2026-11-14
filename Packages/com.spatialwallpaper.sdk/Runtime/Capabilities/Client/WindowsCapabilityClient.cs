using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Runtime.InteropServices;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Capabilities {
 // Trusted SDK transport. No paths, network addresses, credentials or installation selectors.
 public sealed class WindowsCapabilityClient:IDisposable {
  readonly CapabilityMailbox mailbox;readonly WorkerIdentity identity;readonly ResourceLease native;readonly AutoResetEvent wake;readonly Thread reader,writer;IntPtr input,output;int closing,error,done;long receiving;
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool PeekNamedPipe(IntPtr handle,IntPtr buffer,uint size,IntPtr read,out uint available,IntPtr left);
  [DllImport("kernel32.dll",EntryPoint="ReadFile",SetLastError=true)]static extern bool ReadFile(IntPtr handle,IntPtr buffer,uint size,out uint read,IntPtr overlapped);
  [DllImport("kernel32.dll",EntryPoint="WriteFile",SetLastError=true)]static extern bool WriteFile(IntPtr handle,IntPtr buffer,uint size,out uint written,IntPtr overlapped);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll")]static extern uint GetFileType(IntPtr handle);
  public int Error=>Volatile.Read(ref error);public bool Closed=>Volatile.Read(ref closing)!=0;public bool Stopped=>Volatile.Read(ref done)==2;
  public WindowsCapabilityClient(ResourceLedger ledger,WorkerIdentity session,IntPtr requestWriter,IntPtr replyReader){
   if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("capability-platform-unverified");
   if(ledger==null||session==null||requestWriter==IntPtr.Zero||replyReader==IntPtr.Zero||GetFileType(requestWriter)!=3||GetFileType(replyReader)!=3)throw new ArgumentException("capability-client-handles");
   identity=session;input=replyReader;output=requestWriter;bool reading=false,writing=false;
   try{
    native=ledger.Reserve(new ResourceCost(0,2*1024*1024+16384,0,5));var memory=new ManagedReservation(ledger,CapabilityWire.MaxPayloadBytes+16384);mailbox=new CapabilityMailbox(ledger,identity);wake=new AutoResetEvent(false);
    reader=new Thread(ReadLoop){IsBackground=true,Name="SDK capability reader"};writer=new Thread(WriteLoop){IsBackground=true,Name="SDK capability writer"};memory.Bind(this);native.Commit();
    reader.Start();reading=true;writer.Start();writing=true;
   }catch{
    Dispose();if(!reading){if(input!=IntPtr.Zero){CloseHandle(input);input=IntPtr.Zero;}SideDone();}if(!writing){if(output!=IntPtr.Zero){CloseHandle(output);output=IntPtr.Zero;}SideDone();}throw;
   }
  }
  public int Begin(CapabilityOperation operation,byte[] payload){int id=mailbox.Begin(operation,payload);if(id>0)wake.Set();return id;}
  public int State(int id)=>mailbox.State(id);
  public bool TakeReply(int id,out CapabilityResponse response)=>mailbox.TakeReply(id,out response);
  public bool TakePush(CapabilityOperation operation,out CapabilityResponse response)=>mailbox.TakePush(operation,out response);
  void SideDone(){Interlocked.Increment(ref done);}
  ~WindowsCapabilityClient(){wake?.Dispose();native?.Dispose();}
  bool Exact(byte[] bytes,int length){
   int at=0;while(at<length&&Volatile.Read(ref closing)==0){
    if(receiving!=0&&(Stopwatch.GetTimestamp()-receiving)*1000/Stopwatch.Frequency>2000)throw new TimeoutException("capability-reply-deadline");
    uint available;if(!PeekNamedPipe(input,IntPtr.Zero,0,IntPtr.Zero,out available,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    if(available==0){Thread.Sleep(2);continue;}var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
    try{uint read;uint count=(uint)Math.Min((long)available,length-at);if(!ReadFile(input,IntPtr.Add(pin.AddrOfPinnedObject(),at),count,out read,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(read==0||read>count)throw new IOException("capability-empty-read");if(receiving==0)receiving=Stopwatch.GetTimestamp();at+=checked((int)read);}finally{pin.Free();}
   }
   return at==length;
  }
  void ReadLoop(){
   try{var header=new byte[64];var body=new byte[CapabilityWire.MaxPayloadBytes];long sequence=0;
    while(Volatile.Read(ref closing)==0&&Exact(header,header.Length)){var message=CapabilityWire.Validate(header,identity,true,sequence);if(!Exact(body,message.PayloadBytes))break;mailbox.Receive(header,body,message.PayloadBytes);sequence=message.Sequence;receiving=0;}
   }catch{if(Volatile.Read(ref closing)==0){Interlocked.Exchange(ref error,1);Dispose();}}
   finally{if(input!=IntPtr.Zero){CloseHandle(input);input=IntPtr.Zero;}SideDone();}
  }
  void Send(byte[] bytes,int length){
   var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{int at=0;while(at<length){uint written;if(!WriteFile(output,IntPtr.Add(pin.AddrOfPinnedObject(),at),(uint)(length-at),out written,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(written==0||written>length-at)throw new IOException("capability-empty-write");at+=checked((int)written);}}finally{pin.Free();}
  }
  void WriteLoop(){
   try{var header=new byte[64];while(Volatile.Read(ref closing)==0){if(!mailbox.TakeRequest(out var request)){wake.WaitOne(10);continue;}CapabilityWire.Encode(header,identity,request.Operation,false,false,request.Id,request.Sequence,request.Bytes);Send(header,64);Send(request.Buffer,request.Bytes);}}
   catch{if(Volatile.Read(ref closing)==0){Interlocked.Exchange(ref error,1);Dispose();}}
   finally{if(output!=IntPtr.Zero){CloseHandle(output);output=IntPtr.Zero;}SideDone();}
  }
  // No native wait on Unity's render thread. Threads retain their charges until they actually exit.
  public void Dispose(){Interlocked.Exchange(ref closing,1);mailbox?.Close();wake?.Set();}
 }
}

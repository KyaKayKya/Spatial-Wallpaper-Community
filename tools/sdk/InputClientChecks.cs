using System;using System.Threading;using System.Diagnostics;using System.Runtime.InteropServices;
using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Execution;
static class InputClientChecks {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out IntPtr read,out IntPtr write,IntPtr security,uint size);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(IntPtr handle,byte[] bytes,uint size,out uint written,IntPtr overlapped);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")]static extern ulong GetTickCount64();
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Write(IntPtr handle,byte[] bytes){Need(WriteFile(handle,bytes,(uint)bytes.Length,out uint n,IntPtr.Zero)&&n==bytes.Length,"fixture-pipe-write");}
 static void Wait(Func<bool> ready){var clock=Stopwatch.StartNew();while(!ready()){if(clock.ElapsedMilliseconds>2000)throw new Exception("input-client-timeout");Thread.Sleep(2);}}
 static object Query(CapabilityBindings bindings,string method,string result,params object[] args){return bindings.Call("SpatialWallpaper.Sdk::Input::"+method+"("+(args.Length==0?"":"System.String")+")->System."+result,args);}
 static int Main(){IntPtr requests=IntPtr.Zero,requestWriter=IntPtr.Zero,replies=IntPtr.Zero,replyWriter=IntPtr.Zero;WindowsCapabilityClient client=null;try{
  foreach(string method in new[]{"GetButton","GetButtonDown","GetButtonUp"})Need(CapabilityBindings.Supports("SpatialWallpaper.Sdk::Input::"+method+"(System.String)->System.Boolean"),"input-author-member-not-bound:"+method);
  Need(CreatePipe(out requests,out requestWriter,IntPtr.Zero,4096)&&CreatePipe(out replies,out replyWriter,IntPtr.Zero,4096),"fixture-create-pipe");
  var ledger=new ResourceLedger(new ResourceCost(8*1024*1024,4*1024*1024,0,64));var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);
  client=new WindowsCapabilityClient(ledger,identity,requestWriter,replies);requestWriter=replies=IntPtr.Zero;
  var api=new CapabilityCalls(ledger,client);var bindings=new CapabilityBindings(api);long sequence=0;
  Action<ulong,bool,uint,uint,uint,long> send=(epoch,focus,held,down,up,wheel)=>{
   var downs=new uint[7];var ups=new uint[7];downs[2]=down;ups[2]=up;
   byte[] data=InputSnapshotCodec.Encode(epoch,(long)GetTickCount64(),focus,held,(ushort)(focus?65535:0),(ushort)(focus?32768:0),wheel,downs,ups);
   var header=new byte[64];CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,true,false,0,++sequence,data.Length);Write(replyWriter,header);Write(replyWriter,data);
  };
  send(1,true,4,1,0,120);
  Wait(()=>{api.AdvanceFrame();return (bool)Query(bindings,"GetButtonDown","Boolean","Space");});
  Need((bool)Query(bindings,"GetButtonDown","Boolean","Space")&&(bool)Query(bindings,"GetButton","Boolean","Space"),"input-query-not-frame-stable");
  Need((float)Query(bindings,"GetPointerX","Single")==1&&(float)Query(bindings,"GetWheel","Single")==1,"input-pointer-wheel-binding");
  Need(Math.Abs((float)Query(bindings,"GetPointerY","Single")-0.5000076f)<0.000001f,"input-y-binding");
  api.AdvanceFrame();Need(!(bool)Query(bindings,"GetButtonDown","Boolean","Space")&&(bool)Query(bindings,"GetButton","Boolean","Space")&&(float)Query(bindings,"GetWheel","Single")==0,"input-edge-repeated");
  send(1,true,0,1,1,120);Wait(()=>{api.AdvanceFrame();return (bool)Query(bindings,"GetButtonUp","Boolean","Space");});
  send(2,false,0,0,0,0);Wait(()=>{api.AdvanceFrame();return !(bool)Query(bindings,"IsFocused","Boolean");});
  Need((float)Query(bindings,"GetPointerX","Single")==0,"input-pointer-not-revoked");
  send(3,true,4,1,0,0);Wait(()=>{api.AdvanceFrame();return (bool)Query(bindings,"GetButton","Boolean","Space");});
  CloseHandle(replyWriter);replyWriter=IntPtr.Zero;Wait(()=>client.Closed);api.AdvanceFrame();
  Need(!(bool)Query(bindings,"GetButton","Boolean","Space")&&!(bool)Query(bindings,"IsFocused","Boolean"),"disconnected-author-input-retained");
  Console.WriteLine("PASS actual pipe -> shared frame -> author binding, stable edges, pointer/wheel, revocation and disconnect");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{
  client?.Dispose();if(client!=null)Wait(()=>client.Stopped);
  foreach(var handle in new[]{requests,requestWriter,replies,replyWriter})if(handle!=IntPtr.Zero)CloseHandle(handle);
 }}
}

// Original MIT raw-IPC attack fixture. It is not author code and is never shipped as a content Worker.
using System;using System.Text;using System.Runtime.InteropServices;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Execution;
static class CapabilityPeer {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(IntPtr pipe,byte[] bytes,uint length,out uint count,IntPtr unused);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(IntPtr pipe,byte[] bytes,uint length,out uint count,IntPtr unused);
 static void Write(IntPtr handle,byte[] bytes){uint count;if(!WriteFile(handle,bytes,(uint)bytes.Length,out count,IntPtr.Zero)||count!=bytes.Length)throw new Exception("fixture-write");}
 static byte[] Read(IntPtr handle,int length){var bytes=new byte[length];int at=0;while(at<length){var chunk=new byte[length-at];uint count;if(!ReadFile(handle,chunk,(uint)chunk.Length,out count,IntPtr.Zero)||count==0)throw new Exception("fixture-read");Buffer.BlockCopy(chunk,0,bytes,at,(int)count);at+=(int)count;}return bytes;}
 static int Main(string[] args){try{
  IntPtr outgoing=new IntPtr(long.Parse(args[0])),incoming=new IntPtr(long.Parse(args[1])),log=new IntPtr(long.Parse(args[2]));var identity=new WorkerIdentity(Guid.ParseExact(args[3],"N"),Guid.ParseExact(args[4],"N"),1);
  WindowsWorkerBoundary.Require(outgoing,args[6],args[7]);string mode=args[5];long received=0;int calls=0;
  if(mode=="input"||mode=="input-denied"){
   var ledger=new ResourceLedger(new ResourceCost(8*1024*1024,4*1024*1024,0,64));using(var client=new WindowsCapabilityClient(ledger,identity,outgoing,incoming)){
    var api=new CapabilityCalls(ledger,client);var timeout=System.Diagnostics.Stopwatch.StartNew();bool held=false,down=false,cleared=false;int id=api.BeginRead("save");
    while(timeout.ElapsedMilliseconds<1800){api.AdvanceFrame();if(!held&&api.InputButton("Space")){held=true;Write(log,new[]{(byte)'H'});}down|=api.InputDown("Space");
     if(held&&!cleared&&!api.InputFocused()&&!api.InputButton("Space")){cleared=true;var confirmation=new byte[9];confirmation[0]=(byte)'C';Buffer.BlockCopy(BitConverter.GetBytes(WindowsMachineClock.Milliseconds()),0,confirmation,1,8);Write(log,confirmation);}
     if(client.Error!=0)throw new Exception("input-client-fault");Thread.Sleep(5);}
    if(mode=="input"&&(!held||!down||!cleared)||mode=="input-denied"&&(held||down))throw new Exception("input-authorization-or-revocation");
    if(api.State(id)!=3||api.TakeText(id)!="*")throw new Exception("input-starved-storage");
   }Write(log,Encoding.UTF8.GetBytes("input push checked"));return 0;
  }
  if(mode=="audio"||mode=="audio-denied"){
   var ledger=new ResourceLedger(new ResourceCost(8*1024*1024,4*1024*1024,0,64));using(var client=new WindowsCapabilityClient(ledger,identity,outgoing,incoming)){
    var api=new CapabilityCalls(ledger,client);var bindings=new CapabilityBindings(api);var timeout=System.Diagnostics.Stopwatch.StartNew();bool heard=false,cleared=false;int id=api.BeginRead("save");
    while(timeout.ElapsedMilliseconds<1800){api.AdvanceFrame();float value=(float)bindings.Call("SpatialWallpaper.Sdk::Audio::GetBand(System.Int32)->System.Single",new object[]{0});if(value>0)heard=true;if(heard&&value==0)cleared=true;if(client.Error!=0)throw new Exception("audio-client-fault");Thread.Sleep(5);}
    if(mode=="audio"&&(!heard||!cleared)||mode=="audio-denied"&&heard)throw new Exception("audio-authorization-or-freshness");
    if(api.State(id)!=3||api.TakeText(id)!="*")throw new Exception("audio-starved-storage");
   }Write(log,Encoding.UTF8.GetBytes("audio push checked"));return 0;
  }
  if(mode=="sdk-client"){
   var ledger=new ResourceLedger(new ResourceCost(8*1024*1024,4*1024*1024,0,64));using(var client=new WindowsCapabilityClient(ledger,identity,outgoing,incoming)){
    var api=new CapabilityCalls(ledger,client);int id=api.BeginRead("save");var timeout=System.Diagnostics.Stopwatch.StartNew();while(api.State(id)<3){if(client.Error!=0||timeout.ElapsedMilliseconds>5000)throw new Exception("SDK client response timeout");Thread.Sleep(2);}if(api.Version(id)!="1"||api.TakeText(id)!="*")throw new Exception("SDK client read");long nativeCharge=ledger.Used.NativeBytes;client.Dispose();while(!client.Stopped&&timeout.ElapsedMilliseconds<6000)Thread.Sleep(2);if(!client.Stopped)throw new Exception("SDK client did not stop without pending native read");if(ledger.Used.NativeBytes!=nativeCharge)throw new Exception("live-client-wrapper-native-cost-refunded");GC.KeepAlive(client);
   }
   Write(log,Encoding.UTF8.GetBytes("{\"calls\":1}"));return 0;
  }
  if(mode=="wrong")identity=new WorkerIdentity(identity.Attempt,Guid.NewGuid(),1);
  for(int i=0;i<(mode=="healthy"?100:1);i++){
   var operation=mode=="write"?CapabilityOperation.StorageCommit:CapabilityOperation.StorageRead;byte[] body=Encoding.UTF8.GetBytes(mode=="write"?"{\"version\":\"0\",\"changes\":{\"save\":\"Kg==\"}}":"{\"key\":\"save\"}");
   var header=new byte[64];CapabilityWire.Encode(header,identity,operation,false,false,i+1,i+1,body.Length);
   if(mode=="partial"){Write(outgoing,new[]{header[0]});Thread.Sleep(Timeout.Infinite);}
   Write(outgoing,header);Write(outgoing,body);if(mode=="wrong")Thread.Sleep(Timeout.Infinite);
   var response=CapabilityWire.Validate(Read(incoming,64),identity,true,received);received=response.Sequence;var result=JObject.Parse(Encoding.UTF8.GetString(Read(incoming,response.PayloadBytes)));if(response.Error||mode=="healthy"&&(string)result["value"]!="Kg==")throw new Exception("fixture-response:"+result);calls++;if(mode=="healthy")Thread.Sleep(10);
  }
  Write(log,Encoding.UTF8.GetBytes(new JObject{{"calls",calls}}.ToString()));return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

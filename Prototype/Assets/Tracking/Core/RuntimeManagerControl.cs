using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace SpatialTracking {
 // The existing Runtime owner hosts Manager control even when tracking is off.
 // No Tracker is constructed or launched by this constructor or channel.
 public sealed partial class RuntimeTrackingHost {
  Thread managerThread;TcpListener managerListener;TcpClient managerClient;RuntimeLease managerLock;string managerDescriptor;volatile bool managerStop;ulong contentRevision;
  public RuntimeTrackingHost(){}
  public void StartManagerControl(string directory,Func<JObject,Task<JObject>> execute){
   if(managerThread!=null)throw new InvalidOperationException("manager-control-already-started");Directory.CreateDirectory(directory);
   RuntimeDirectory.RestrictToCurrentUser(directory);
   managerLock=RuntimeLease.TryAcquire(Path.Combine(directory,"runtime.lock"));if(managerLock==null)throw new IOException("runtime-already-running");
   managerListener=new TcpListener(IPAddress.Loopback,0);managerListener.Start(1);string token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");managerDescriptor=Path.Combine(directory,"connection.json");
   // ReplaceFile preserves destination security. Restrict a stale descriptor
   // before publishing new token bytes, even if it had explicit old ACLs.
   if(File.Exists(managerDescriptor))RuntimeDirectory.RestrictToCurrentUser(managerDescriptor);
   string temp=managerDescriptor+".tmp";File.WriteAllText(temp,new JObject{["protocol_version"]=1,["port"]=((IPEndPoint)managerListener.LocalEndpoint).Port,["pid"]=System.Diagnostics.Process.GetCurrentProcess().Id,["runtime_instance_id"]=runtime,["token"]=token}.ToString());RuntimeDirectory.RestrictToCurrentUser(temp);if(File.Exists(managerDescriptor))File.Replace(temp,managerDescriptor,null);else File.Move(temp,managerDescriptor);
   managerThread=new Thread(()=>ManagerLoop(token,execute)){IsBackground=true,Name="Spatial Runtime Manager TCP"};managerThread.Start();
  }
  void ManagerLoop(string token,Func<JObject,Task<JObject>> execute){
   while(!managerStop){
    try{managerClient=managerListener.AcceptTcpClient();managerClient.NoDelay=true;managerClient.ReceiveTimeout=5000;managerClient.SendTimeout=3000;using(var connectionStream=managerClient.GetStream()){
     ulong rx=0,tx=0;string connection=Guid.NewGuid().ToString();var hello=TrackingWire.Read(connectionStream);ValidateEnvelope(hello,null,null,ref rx);
     if((string)hello["kind"]!="handshake"||(string)hello["type"]!="Hello"||hello["connection_id"].Type!=JTokenType.Null||(string)hello["payload"]["role"]!="manager"||(string)hello["payload"]["token"]!=token)throw new InvalidDataException("INVALID_MANAGER_HELLO");string instance=TrackingWire.Id(hello["sender_instance_id"]);
     var versions=hello["payload"]["protocol_versions"] as JArray;if(versions==null||versions.Count<1||versions.Count>8||!versions.Any(v=>v.Type==JTokenType.Integer&&(int)v==1))throw new InvalidDataException("INCOMPATIBLE_MANAGER_PROTOCOL");
     Action<string,string,string,JObject> send=(kind,type,request,payload)=>TrackingWire.Write(connectionStream,new JObject{["protocol_version"]=1,["kind"]=kind,["type"]=type,["runtime_instance_id"]=runtime,["sender_instance_id"]=runtime,["connection_id"]=connection,["seq"]=(++tx).ToString(),["request_id"]=request,["sent_at_us"]=QpcClock.Now.ToString(),["state_revision"]=(++contentRevision).ToString(),["payload"]=payload});
     send("handshake","Welcome",null,new JObject{["protocol_version"]=1,["connection_id"]=connection});managerClient.ReceiveTimeout=0;
     while(!managerStop){var command=TrackingWire.Read(connectionStream);ValidateEnvelope(command,instance,connection,ref rx);if((string)command["kind"]!="command")throw new InvalidDataException("INVALID_MANAGER_COMMAND");string request=(string)command["request_id"];
      send("response","OperationResult",request,new JObject{["status"]="accepted"});JObject result;
      try{result=execute(command).GetAwaiter().GetResult();send("response","OperationResult",request,new JObject{["status"]="completed",["result"]=result});}
      catch(Exception){send("response","OperationResult",request,new JObject{["status"]="failed",["error"]=new JObject{["code"]="CONTENT_OPERATION_FAILED",["message"]="内容操作失败，原登记状态保留"}});}
     }
    }}catch(Exception){if(!managerStop)Diagnostic="MANAGER_DISCONNECTED";}finally{managerClient?.Dispose();managerClient=null;}
   }
  }
  void StopManagerControl(){managerStop=true;managerClient?.Close();managerListener?.Stop();if(managerThread!=null&&!managerThread.Join(3500))Diagnostic="MANAGER_STOP_TIMEOUT";if(managerDescriptor!=null&&File.Exists(managerDescriptor))File.Delete(managerDescriptor);managerLock?.Dispose();}
 }
}

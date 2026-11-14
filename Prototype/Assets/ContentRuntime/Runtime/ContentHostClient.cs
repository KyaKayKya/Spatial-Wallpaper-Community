using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
namespace SpatialWallpaper.Content {
 public sealed class ContentOperationException:IOException {public ContentOperationException():base("content-operation-failed"){} }
 public sealed class ContentHostClient:IDisposable {
  readonly TcpClient client=new TcpClient();readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);readonly string runtime,instance=Guid.NewGuid().ToString();string connection;ulong tx,rx,revision;
  public Action<string> Status;
  public ContentHostClient(string descriptor){
   JObject data;using(var input=new FileStream(descriptor,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(input))data=JObject.Parse(reader.ReadToEnd());if((int)data["protocol_version"]!=1)throw new InvalidDataException("unsupported-runtime");runtime=TrackingWire.Id(data["runtime_instance_id"]);int port=(int)data["port"];if(port<1||port>65535)throw new InvalidDataException("invalid-runtime-port");client.Connect(IPAddress.Loopback,port);client.NoDelay=true;client.ReceiveTimeout=40000;client.SendTimeout=5000;
   TrackingWire.Write(client.GetStream(),Envelope("handshake","Hello",null,new JObject{["role"]="manager",["token"]=(string)data["token"],["protocol_versions"]=new JArray(1)}));var welcome=Read();if((string)welcome["type"]!="Welcome")throw new InvalidDataException("runtime-handshake-failed");connection=TrackingWire.Id(welcome["payload"]["connection_id"]);
  }
  JObject Envelope(string kind,string type,string request,JObject payload)=>new JObject{["protocol_version"]=1,["kind"]=kind,["type"]=type,["runtime_instance_id"]=runtime,["sender_instance_id"]=instance,["connection_id"]=connection,["seq"]=(++tx).ToString(),["request_id"]=request,["sent_at_us"]=QpcClock.Now.ToString(),["state_revision"]=null,["payload"]=payload};
  JObject Read(){var packet=TrackingWire.Read(client.GetStream());if((int)packet["protocol_version"]!=1||(string)packet["runtime_instance_id"]!=runtime||(string)packet["sender_instance_id"]!=runtime||TrackingWire.Decimal(packet["seq"])!=++rx||(connection!=null&&(string)packet["connection_id"]!=connection))throw new InvalidDataException("runtime-envelope-mismatch");ulong next=TrackingWire.Decimal(packet["state_revision"]);if(next<=revision)throw new InvalidDataException("stale-runtime-state");revision=next;return packet;}
  public async Task<JObject> Call(string type,JObject payload){await gate.WaitAsync();try{return await Task.Run(()=>{string id=Guid.NewGuid().ToString();TrackingWire.Write(client.GetStream(),Envelope("command",type,id,payload));while(true){var packet=Read();if((string)packet["request_id"]!=id||(string)packet["type"]!="OperationResult")throw new InvalidDataException("runtime-request-mismatch");var result=(JObject)packet["payload"];string status=(string)result["status"];Status?.Invoke(status);if(status=="failed")throw new ContentOperationException();if(status=="completed")return (JObject)result["result"];if(status!="accepted")throw new InvalidDataException("invalid-operation-status");}});}finally{gate.Release();}}
  public async Task<JObject> Library(){
   for(int attempt=0;attempt<3;attempt++)try{var first=await Call("GetContentState",new JObject());var page=first;while(page["page"]["next"].Type!=JTokenType.Null){page=await Call("GetContentState",new JObject{["after"]=page["page"]["next"].DeepClone(),["snapshot"]=first["page"]["snapshot"].DeepClone()});foreach(var setting in ((JObject)page["sound_mixes"]).Properties())first["sound_mixes"][setting.Name]=setting.Value.DeepClone();if(page["target_sound_mixes"] is JObject targetSettings){if(!(first["target_sound_mixes"] is JObject))first["target_sound_mixes"]=new JObject();foreach(var setting in targetSettings.Properties())first["target_sound_mixes"][setting.Name]=setting.Value.DeepClone();}foreach(string group in new[]{"works","builds","revisions"})foreach(var item in ((JObject)page["state"][group]).Properties())first["state"][group][item.Name]=item.Value.DeepClone();}return first;}catch(ContentOperationException){if(attempt==2)throw;}
   throw new ContentOperationException();
  }
  public void Dispose(){client.Dispose();}
 }
}

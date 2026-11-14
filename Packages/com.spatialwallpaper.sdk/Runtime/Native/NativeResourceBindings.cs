using System;using System.Collections.Generic;using System.Linq;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Native {
 // Script-thread request state. Ready comes exclusively from a renderer ACK.
 public sealed class NativeResourceBindings:IDisposable {
  sealed class Request {internal int Id,State=1;internal long Target,Epoch;internal string Kind,Error;internal ResourceLease Memory;}
  readonly OwnedDomain domain;readonly Action<SceneCommand> queue;readonly Func<object,string,long> resolve;readonly Dictionary<int,Request> requests=new Dictionary<int,Request>();int next;long epoch;Request sent;string sentAction;
  public NativeResourceBindings(OwnedDomain domain,Action<SceneCommand> queue,Func<object,string,long> resolve){this.domain=domain;this.queue=queue;this.resolve=resolve;}
  public long ExpectedResult=>sent==null?0:-sent.Id;
  public void Advance(SceneSnapshot snapshot){long current=(long?)snapshot.Objects.FirstOrDefault(x=>x.IsNode)?.State["resource_epoch"]??0;if(current!=epoch){epoch=current;foreach(var r in requests.Values)if(r.State!=3)r.State=4;}sent=null;sentAction=null;}
  void Send(Request r,string action,JToken payload=null){if(sent!=null)throw new InvalidOperationException("native-resource-one-operation-per-tick");if(action=="release")r.Epoch=epoch;if(r.Epoch!=epoch||epoch<1)throw new InvalidOperationException("native-resource-revoked");queue(new SceneCommand("Resource",0,new JArray(epoch,r.Id,action,r.Target,payload??new JArray())));sent=r;sentAction=action;}
  public void Poll(){if(sent!=null)return;var request=requests.Values.FirstOrDefault(r=>r.State==1);if(request!=null)Send(request,"poll");}
  Request Get(int id){if(!requests.TryGetValue(id,out var r))throw new InvalidOperationException("native-resource-unknown-request");return r;}
  public object Call(string action,object[] args){
   if(action=="ResourceBeginMesh"||action=="ResourceBeginTexture"){
    if(requests.Count>=64||next==int.MaxValue||epoch<1)throw new InvalidOperationException("native-resource-request-budget-or-unavailable");bool mesh=action=="ResourceBeginMesh";long target=resolve(args[0],mesh?"node":"material");JToken payload;
    if(mesh){var counts=(OwnedArray)args[2];domain.RequireType(counts,"i32[]");var values=counts.Items.Cast<int>().ToArray();NativeUploadSpec.Mesh((int)args[1],values);payload=new JArray((int)args[1],new JArray(values));}
    else{NativeUploadSpec.Texture((int)args[2],(int)args[3]);payload=new JArray((string)args[1],(int)args[2],(int)args[3]);}
    var memory=domain.Ledger.Reserve(new ResourceCost(2048,0,0,0));var request=new Request{Id=++next,Target=target,Epoch=epoch,Kind=mesh?"mesh":"texture",Memory=memory};try{Send(request,mesh?"begin_mesh":"begin_texture",payload);requests.Add(request.Id,request);memory.Commit();return request.Id;}catch{memory.Dispose();throw;}
   }
   int id=(int)args[0];if(action=="ResourceState"&&!requests.ContainsKey(id))return 0;if(action=="ResourceError"&&!requests.ContainsKey(id))return null;var r=Get(id);
   if(action=="ResourceState")return r.State;if(action=="ResourceError")return r.Error;
   if(action=="ResourceWriteFloats"||action=="ResourceWriteIndices"||action=="ResourceWriteBytes"){
    if(r.State!=1)throw new InvalidOperationException("native-resource-not-receiving");var values=(OwnedArray)args[2];bool floats=action=="ResourceWriteFloats",bytes=action=="ResourceWriteBytes";domain.RequireType(values,floats?"f32[]":"i32[]");int count=values.Items.Length;if(count<1||count>(bytes?16384:4096))throw new InvalidOperationException("native-resource-block-limit");var data=new byte[count*(bytes?1:4)];for(int i=0;i<count;i++){if(bytes){int v=(int)values.Items[i];if(v<0||v>255)throw new InvalidOperationException("native-resource-byte-range");data[i]=(byte)v;}else Buffer.BlockCopy(floats?BitConverter.GetBytes((float)values.Items[i]):BitConverter.GetBytes((int)values.Items[i]),0,data,i*4,4);}Send(r,"chunk",new JArray((int)args[1],Convert.ToBase64String(data)));return null;
   }
   if(action=="ResourceApply"&&r.State!=2)throw new InvalidOperationException("native-resource-apply-before-ready");if(action=="ResourceSeal"&&r.State!=1)throw new InvalidOperationException("native-resource-seal-state");if(action=="ResourceCancel"&&r.State==3)throw new InvalidOperationException("native-resource-published-requires-release");Send(r,action=="ResourceSeal"?"seal":action=="ResourceApply"?"apply":action=="ResourceCancel"?"cancel":"release");return null;
  }
  public void Acknowledge(IReadOnlyDictionary<long,JToken> results,SceneSnapshot snapshot){if(sent==null)return;var r=sent;if(!results.TryGetValue(-r.Id,out var value))throw new InvalidOperationException("native-resource-missing-ack");var reply=value as JObject;JsonData.Fields(reply,"state","error","epoch");int state=JsonData.Integer(reply["state"],1,5);long remote=(long)reply["epoch"];if(reply["error"].Type!=JTokenType.Null)JsonData.Text(reply["error"],1024);long current=(long?)snapshot.Objects.FirstOrDefault(x=>x.IsNode)?.State["resource_epoch"]??0;if(r.Epoch!=current||remote!=r.Epoch){r.State=4;r.Error="revoked";}else{r.State=state;r.Error=(string)reply["error"];if(sentAction=="release"){r.Memory.Dispose();requests.Remove(r.Id);}}sent=null;sentAction=null;}
  public void Dispose(){foreach(var request in requests.Values)request.Memory.Dispose();requests.Clear();}
 }
}

using System;using System.Collections.Generic;using System.Linq;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;
namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativeResourceCommandTarget:IDisposable {
  sealed class Request {internal int Id;internal long Token,Epoch,Target;internal string Kind,Property,Error;internal MeshFilter Filter;internal Material Material;internal UnityEngine.Object Previous;internal bool Applied,Canceled,Sealed;}
  readonly SessionIdentity identity;readonly NativeDynamicResources resources;readonly Func<long,Transform> node;readonly Func<long,Material> material;readonly Dictionary<int,Request> requests=new Dictionary<int,Request>();bool disposed;
  public NativeResourceCommandTarget(SessionIdentity identity,NativeDynamicResources resources,Func<long,Transform> node,Func<long,Material> material){this.identity=identity;this.resources=resources;this.node=node;this.material=material;}
  public long Epoch=>resources.AuthorityEpoch;
  public bool CanDisposeOwner=>resources.CanDisposeOwner;
  public Action Prepare(JArray args,Action<long,JToken> reply){NativeResourceRules.Check(args);if(disposed)throw new ObjectDisposedException(nameof(NativeResourceCommandTarget));string action=(string)args[2];long target=(long)args[3];if(action=="begin_mesh"){var n=node(target);if(!n.GetComponent<MeshFilter>())throw new InvalidOperationException("native-resource-MeshFilter-required");}if(action=="begin_texture"){var m=material(target);string property=(string)args[4][0];int index=m.shader.FindPropertyIndex(property);if(index<0||m.shader.GetPropertyType(index)!=ShaderPropertyType.Texture)throw new InvalidOperationException("native-resource-texture-property-required");}return ()=>reply(-(int)args[1],Execute(args));}
  JToken Execute(JArray args){int id=(int)args[1];long epoch=(long)args[0];string action=(string)args[2];long target=(long)args[3];var payload=(JArray)args[4];requests.TryGetValue(id,out var r);if(epoch!=Epoch)return Result(4,"revoked",Epoch);if(r!=null&&(r.Target!=target||r.Epoch!=epoch&&action!="release"))return Result(4,"revoked-or-target-changed",Epoch);
   if(r?.Error!=null&&action!="release")return Result(5,r.Error,Epoch);
   try{
    if(action=="begin_mesh"||action=="begin_texture"){
     if(r!=null||requests.Count>=64)throw new InvalidOperationException("native-resource-duplicate-or-budget");r=new Request{Id=id,Epoch=epoch,Target=target,Kind=action=="begin_mesh"?"mesh":"texture"};requests.Add(id,r);
     if(r.Kind=="mesh"){r.Filter=node(target).GetComponent<MeshFilter>();r.Previous=r.Filter.sharedMesh;r.Token=resources.BeginMesh(identity.Instance,identity.Generation,epoch,(int)payload[0],((JArray)payload[1]).Select(x=>(int)x).ToArray());}
     else{r.Material=material(target);r.Property=(string)payload[0];r.Previous=r.Material.GetTexture(r.Property);r.Token=resources.BeginTexture(identity.Instance,identity.Generation,epoch,(int)payload[1],(int)payload[2]);}
    }else{if(r==null)throw new InvalidOperationException("native-resource-unknown-request");if(action=="chunk"){if(r.Sealed||r.Canceled||r.Applied)throw new InvalidOperationException("native-resource-chunk-state");resources.Append(r.Token,epoch,(int)payload[0],Convert.FromBase64String((string)payload[1]));}else if(action=="seal"){if(r.Sealed)throw new InvalidOperationException("native-resource-already-sealed");resources.Seal(r.Token,epoch);r.Sealed=true;}else if(action=="apply"){
      if(r.Applied||r.Canceled||requests.Values.Any(x=>x!=r&&x.Applied&&x.Target==r.Target&&(x.Kind=="mesh"||x.Property==r.Property)))throw new InvalidOperationException("native-resource-binding-already-owned");resources.Publish(r.Token,value=>Bind(r,value));r.Applied=true;
     }else if(action=="cancel"){if(r.Applied)throw new InvalidOperationException("native-resource-applied-requires-release");resources.Cancel(r.Token,epoch);r.Canceled=true;}else if(action=="release"){if(r.Applied){resources.Retire(r.Token,()=>Bind(r,r.Previous));r.Applied=false;}else if(!r.Canceled&&r.Token>0&&r.Epoch==Epoch)resources.Cancel(r.Token,epoch);r.Canceled=true;requests.Remove(id);return Result(4,null,Epoch);}
    }
    return Status(r);
   }catch(Exception error){if(r!=null)r.Error=error.Message;return Result(5,error.Message,Epoch);}
  }
  void Bind(Request request,UnityEngine.Object value){if(request.Kind=="mesh"){if(!request.Filter)throw new InvalidOperationException("native-resource-target-destroyed");request.Filter.sharedMesh=(Mesh)value;}else{if(!request.Material)throw new InvalidOperationException("native-resource-target-destroyed");request.Material.SetTexture(request.Property,(Texture)value);}}
  static JObject Result(int state,string error,long epoch)=>new JObject{{"state",state},{"error",error==null?JValue.CreateNull():new JValue(error)},{"epoch",epoch}};
  JToken Status(Request r){if(r.Error!=null)return Result(5,r.Error,Epoch);if(r.Applied)return Result(3,null,Epoch);if(r.Canceled||r.Epoch!=Epoch)return Result(4,"canceled",Epoch);var state=resources.State(r.Token);return Result(state==NativeUploadPhase.Ready?2:state==NativeUploadPhase.Failed?5:1,resources.Error(r.Token),Epoch);}
  public void Step(){resources.Step();foreach(var r in requests.Values){if(r.Token<1||r.Canceled||r.Applied||r.Epoch!=Epoch||r.Error!=null)continue;var error=resources.Error(r.Token);if(error!=null)r.Error=error;}}
  public void Revoke(long nextEpoch)=>resources.Revoke(nextEpoch);
  public void Dispose(){if(disposed)return;foreach(var r in requests.Values.Where(r=>r.Applied)){resources.Retire(r.Token,()=>{if(r.Kind=="mesh"&&r.Filter||r.Kind=="texture"&&r.Material)Bind(r,r.Previous);});r.Applied=false;}requests.Clear();resources.Dispose();disposed=true;}
 }
}

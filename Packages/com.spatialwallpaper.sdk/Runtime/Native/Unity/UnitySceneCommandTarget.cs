using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpatialWallpaper.Sdk.Native.Unity {
 // A renderer-owned table. No name lookup, author reflection, camera handle,
 // foreign scene object or UnityEngine.Object enters the script process.
 public sealed class UnitySceneCommandTarget:INativeCommandTarget,IDisposable {
  readonly NativeResourceCommandTarget resourceCommands;
  readonly SessionIdentity identity;readonly Transform[] nodes;readonly Dictionary<Transform,long> ids;
  readonly SceneObject[] cachedObjects;readonly NodeSample[] cachedSamples;
  readonly NativeAssetCatalog assets;readonly NativeSoundBus sound;
  readonly NativePrefabPool prefabs;readonly HashSet<long> retired=new HashSet<long>();readonly Dictionary<long,DynamicNode> dynamicNodes=new Dictionary<long,DynamicNode>();long nextId;Preparation pendingPreparation;
  readonly int owner;long revision=1,sequence;bool disposed;
  sealed class Preparation {internal CommandBatch Batch;internal Action[] Apply;internal Dictionary<long,JToken> Results;internal readonly Dictionary<long,long> Mapping=new Dictionary<long,long>();internal readonly Dictionary<long,Transform> Temporary=new Dictionary<long,Transform>();internal readonly List<NativePrefabPool.Lease> Leases=new List<NativePrefabPool.Lease>();}
  sealed class DynamicNode {internal long Id;internal Transform Node;internal NativePrefabPool.Lease Lease;internal SceneObject Cached;internal NodeSample Sample;}
  [StructLayout(LayoutKind.Explicit)]struct FloatBits {[FieldOffset(0)]internal float Value;[FieldOffset(0)]internal int Bits;}
  static bool Exact(float a,float b)=>new FloatBits{Value=a}.Bits==new FloatBits{Value=b}.Bits;
  static bool Exact(Vector3 a,Vector3 b)=>Exact(a.x,b.x)&&Exact(a.y,b.y)&&Exact(a.z,b.z);
  static bool Exact(Quaternion a,Quaternion b)=>Exact(a.x,b.x)&&Exact(a.y,b.y)&&Exact(a.z,b.z)&&Exact(a.w,b.w);
  struct NodeSample {
   internal Vector3 Position,Scale;internal Quaternion Rotation;internal bool Active,Light;internal float Intensity;internal long Parent,ResourceEpoch;
   internal bool Same(NodeSample other)=>ResourceEpoch==other.ResourceEpoch&&Parent==other.Parent&&Active==other.Active&&Light==other.Light&&Exact(Position,other.Position)&&Exact(Rotation,other.Rotation)&&Exact(Scale,other.Scale)&&Exact(Intensity,other.Intensity);
  }
  public UnitySceneCommandTarget(SessionIdentity identity,IEnumerable<Transform> ownedNodes,NativeAssetCatalog assets=null,NativeSoundBus sound=null,NativePrefabPool prefabs=null,NativeDynamicResources dynamicResources=null){
   this.identity=identity??throw new ArgumentNullException(nameof(identity));identity.Require(identity);owner=Thread.CurrentThread.ManagedThreadId;
   nodes=ownedNodes?.Take(SceneCommandCatalog.MaxObjects+1).ToArray()??throw new ArgumentNullException(nameof(ownedNodes));
   if(nodes.Length==0||nodes.Length>SceneCommandCatalog.MaxObjects||nodes.Any(t=>!t||t.GetComponentInChildren<Camera>(true))||nodes.Distinct().Count()!=nodes.Length)throw new ArgumentException("native-owned-node-table");
   cachedObjects=new SceneObject[nodes.Length];cachedSamples=new NodeSample[nodes.Length];
   ids=new Dictionary<Transform,long>();for(int i=0;i<nodes.Length;i++)ids.Add(nodes[i],i+1);
   foreach(var node in nodes)if(node.parent&&ids.TryGetValue(node.parent,out long parent)&&parent>=ids[node])throw new ArgumentException("native-owned-node-order");
   this.assets=assets;this.sound=sound;this.prefabs=prefabs;if(dynamicResources!=null)resourceCommands=new NativeResourceCommandTarget(identity,dynamicResources,id=>RequireNode(id,null),id=>RequireAsset<Material>(id,"material"));if(assets!=null&&assets.IsBound){if(assets.Definitions.Any(value=>value.Id<=nodes.Length))throw new ArgumentException("native-resource-node-id-overlap");}else assets?.Bind(nodes.Length+1);nextId=assets!=null&&assets.Definitions.Count>0?assets.Definitions.Max(value=>value.Id)+1:nodes.Length+1;
  }
  void RequireOwner(){if(disposed)throw new ObjectDisposedException(nameof(UnitySceneCommandTarget));if(Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("native-Unity-thread");}
  static JArray Vector(Vector3 value)=>new JArray(value.x,value.y,value.z);
  static JArray Rotation(Quaternion value)=>new JArray(value.x,value.y,value.z,value.w);
  public SceneSnapshot Snapshot {get{
   RequireOwner();var values=new List<SceneObject>(nodes.Length);
   for(int i=0;i<nodes.Length;i++){
    if(retired.Contains(i+1))continue;
    var node=nodes[i];if(!node)throw new InvalidOperationException("native-owned-node-destroyed");var light=node.GetComponent<Light>();
    long parent=node.parent&&ids.TryGetValue(node.parent,out long id)?id:0;
    var sample=new NodeSample{Position=node.localPosition,Rotation=node.localRotation,Scale=node.localScale,Active=node.gameObject.activeSelf,Light=light,Intensity=light?light.intensity:0,Parent=parent,ResourceEpoch=i==0?(resourceCommands?.Epoch??0):0};
    // SceneObject is immutable and already validated. Reuse unchanged values
    // instead of reserializing/parsing them twice per frame; always sample
    // Unity first so animation, hierarchy and component changes remain visible.
    if(cachedObjects[i]==null||!sample.Same(cachedSamples[i])){
     var state=new JObject{{"position",Vector(sample.Position)},{"rotation",Rotation(sample.Rotation)},{"scale",Vector(sample.Scale)},{"active",sample.Active}};
     if(i==0&&resourceCommands!=null)state["resource_epoch"]=resourceCommands.Epoch;
     if(sample.Light)state["intensity"]=sample.Intensity;
     var value=new SceneObject(i+1,sample.Light?"light":"node",parent,state);cachedSamples[i]=sample;cachedObjects[i]=value;
    }
    values.Add(cachedObjects[i]);
   }
   if(assets!=null&&assets.Definitions.Count>0)values.AddRange(assets.Definitions);
   if(dynamicNodes.Count>0)foreach(var record in dynamicNodes.Values.OrderBy(value=>value.Id)){
    var node=record.Node;if(!node)throw new InvalidOperationException("native-owned-node-destroyed");var light=node.GetComponent<Light>();long parent=node.parent&&ids.TryGetValue(node.parent,out var id)?id:0;var sample=new NodeSample{Position=node.localPosition,Rotation=node.localRotation,Scale=node.localScale,Active=node.gameObject.activeSelf,Light=light,Intensity=light?light.intensity:0,Parent=parent};
    if(record.Cached==null||!sample.Same(record.Sample)){var state=new JObject{{"position",Vector(sample.Position)},{"rotation",Rotation(sample.Rotation)},{"scale",Vector(sample.Scale)},{"active",sample.Active}};if(sample.Light)state["intensity"]=sample.Intensity;record.Cached=new SceneObject(record.Id,sample.Light?"light":"node",parent,state);record.Sample=sample;}values.Add(record.Cached);
   }
   return new SceneSnapshot(revision,checked(++sequence),values);
  }}
  public bool TryPrepare(CommandBatch batch,out object prepared){
   RequireOwner();identity.Require(batch.Identity);if(batch.BaseRevision!=revision||pendingPreparation!=null)throw new InvalidOperationException("native-scene-revision-or-pending-preparation");
   var actions=new List<Action>();var results=new Dictionary<long,JToken>();
   var transaction=new Preparation{Batch=batch,Results=results};var musicLayers=new HashSet<AudioClip>(sound?.MusicClips??new AudioClip[0]);try{
   // Resolve and validate the entire batch before the first visible change.
   foreach(var command in batch.Commands){
    var args=command.Arguments;
    switch(command.Operation){
     case "Resource":{if(batch.Commands.Count!=1||resourceCommands==null)throw new InvalidOperationException("native-resource-unavailable-or-nonexclusive");actions.Add(resourceCommands.Prepare(args,(id,value)=>results.Add(id,value)));continue;}
     case "Instantiate":{
      if(prefabs==null||assets==null)throw new InvalidOperationException("native-prefab-pool-missing");var definition=assets.Definitions.SingleOrDefault(value=>value.Id==command.Target&&value.Kind=="prefab");if(definition==null||definition.Template.Count!=args.Count)throw new InvalidOperationException("native-prefab-layout-mismatch");if(nodes.Length-retired.Count+dynamicNodes.Count+assets.Definitions.Count+transaction.Temporary.Count+args.Count>SceneCommandCatalog.MaxObjects)throw new InvalidOperationException("native-object-budget");var lease=prefabs.Borrow(command.Target);transaction.Leases.Add(lease);if(lease.Nodes.Length!=args.Count)throw new InvalidOperationException("native-prefab-template-changed");var records=new List<DynamicNode>();
      for(int index=0;index<args.Count;index++){long temporary=(long)args[index];if(temporary>=0||nextId>int.MaxValue)throw new InvalidOperationException("native-temporary-id");long actual=nextId++;transaction.Mapping.Add(temporary,actual);transaction.Temporary.Add(temporary,lease.Nodes[index]);records.Add(new DynamicNode{Id=actual,Node=lease.Nodes[index],Lease=lease});}
      actions.Add(()=>{foreach(var record in records){dynamicNodes.Add(record.Id,record);ids.Add(record.Node,record.Id);}prefabs.Publish(lease);});continue;
     }
     case "MaterialFloat":{var material=RequireAsset<Material>(command.Target,"material");string name=(string)args[0];if(!material.HasProperty(name))throw new InvalidOperationException("native-material-property-not-owned");float value=(float)args[1];actions.Add(()=>material.SetFloat(name,value));continue;}
     case "MaterialColor":{var material=RequireAsset<Material>(command.Target,"material");string name=(string)args[0];if(!material.HasProperty(name))throw new InvalidOperationException("native-material-property-not-owned");var values=(JArray)args[1];var color=new Color((float)values[0],(float)values[1],(float)values[2],(float)values[3]);actions.Add(()=>material.SetColor(name,color));continue;}
     case "PlayMusic":case "PlayEffect":{var clip=RequireAsset<AudioClip>(command.Target,"sound");if(sound==null)throw new InvalidOperationException("native-sound-owner-missing");float gain=(float)args[0];sound.RequirePrepared(clip,gain);if(command.Operation=="PlayMusic"){musicLayers.Add(clip);if(musicLayers.Count>NativeSoundBus.MaxMusicLayers)throw new InvalidOperationException("native-music-layer-budget");actions.Add(()=>sound.PlayMusic(clip,gain));}else actions.Add(()=>sound.PlayEffect(clip,gain));continue;}
     case "StopMusic":case "StopEffects":{if(command.Target!=0||sound==null)throw new InvalidOperationException("native-sound-stop-owner");if(command.Operation=="StopMusic"){musicLayers.Clear();actions.Add(sound.StopMusic);}else actions.Add(sound.StopEffects);continue;}
    }
    var node=RequireNode(command.Target,transaction);
    switch(command.Operation){
     case "SetPosition":{var v=new Vector3((float)args[0],(float)args[1],(float)args[2]);actions.Add(()=>node.localPosition=v);break;}
     case "SetRotation":{var q=new Quaternion((float)args[0],(float)args[1],(float)args[2],(float)args[3]);actions.Add(()=>node.localRotation=q);break;}
     case "SetScale":{var v=new Vector3((float)args[0],(float)args[1],(float)args[2]);actions.Add(()=>node.localScale=v);break;}
     case "SetActive":{bool active=(bool)args[0];actions.Add(()=>node.gameObject.SetActive(active));break;}
     case "Destroy":{actions.Add(()=>DestroyNode(node));break;}
     case "SetLightIntensity":{var light=node.GetComponent<Light>();if(!light)throw new InvalidOperationException("native-light-not-owned");float intensity=(float)args[0];actions.Add(()=>light.intensity=intensity);break;}
     case "AnimatorFloat":{var animator=RequireAnimator(node,(string)args[0],AnimatorControllerParameterType.Float);float value=(float)args[1];if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("native-animator-value");string name=(string)args[0];actions.Add(()=>animator.SetFloat(name,value));break;}
     case "AnimatorBool":{var animator=RequireAnimator(node,(string)args[0],AnimatorControllerParameterType.Bool);string name=(string)args[0];bool value=(bool)args[1];actions.Add(()=>animator.SetBool(name,value));break;}
     case "AnimatorTrigger":{var animator=RequireAnimator(node,(string)args[0],AnimatorControllerParameterType.Trigger);string name=(string)args[0];actions.Add(()=>animator.SetTrigger(name));break;}
     case "Query":{string name=(string)args[0];long id=(long)args[1];var arguments=(JArray)args[2];SceneCommandRules.CheckQueryArguments(name,arguments);actions.Add(()=>results.Add(id,ReadQuery(node,name,arguments)));break;}
     default:throw new NotSupportedException("native-operation-not-installed:"+command.Operation);
    }
   }
   transaction.Apply=actions.ToArray();pendingPreparation=transaction;prepared=transaction;return true;
   }catch{foreach(var lease in transaction.Leases)prefabs.Return(lease);throw;}
  }
  Transform RequireNode(long id,Preparation transaction){Transform node;if(id<0){if(!transaction.Temporary.TryGetValue(id,out node))throw new InvalidOperationException("native-node-not-owned");}else if(id>=1&&id<=nodes.Length&&!retired.Contains(id))node=nodes[(int)id-1];else if(dynamicNodes.TryGetValue(id,out var record))node=record.Node;else throw new InvalidOperationException("native-node-not-owned");if(!node)throw new InvalidOperationException("native-owned-node-destroyed");return node;}
  void DestroyNode(Transform node){
   var removed=ids.Where(pair=>pair.Key==node||pair.Key.IsChildOf(node)).ToArray();NativePrefabPool.Lease lease=null;if(ids.TryGetValue(node,out var id)&&dynamicNodes.TryGetValue(id,out var record))lease=record.Lease;
   node.gameObject.SetActive(false);foreach(var pair in removed){ids.Remove(pair.Key);if(pair.Value<=nodes.Length)retired.Add(pair.Value);else dynamicNodes.Remove(pair.Value);}
   if(lease!=null&&lease.Root==node.gameObject)prefabs.Return(lease);else{if(lease!=null)prefabs.InvalidateSubtree(lease);UnityEngine.Object.Destroy(node.gameObject);}
  }
  T RequireAsset<T>(long id,string kind)where T:UnityEngine.Object{if(assets==null)throw new InvalidOperationException("native-resource-catalog-missing");return assets.Require<T>(id,kind);}
  static Animator RequireAnimator(Transform node,string name,AnimatorControllerParameterType type){var animator=node.GetComponent<Animator>();if(!animator||!animator.runtimeAnimatorController)throw new InvalidOperationException("native-animator-not-owned");if(!animator.parameters.Any(parameter=>parameter.name==name&&parameter.type==type))throw new InvalidOperationException("native-animator-parameter-not-owned");return animator;}
  static JObject QueryError(string error)=>new JObject{{"status","error"},{"error",error}};
  static JObject QueryValue(JToken value)=>new JObject{{"status","ok"},{"value",value}};
  JToken ReadQuery(Transform node,string name,JArray arguments){
   if(!node)return QueryError("destroyed");
   switch(name){
    case "bounds":{var renderers=node.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return QueryError("unavailable");var bounds=renderers[0].bounds;for(int index=1;index<renderers.Length;index++)bounds.Encapsulate(renderers[index].bounds);return QueryValue(new JArray(bounds.center.x,bounds.center.y,bounds.center.z,bounds.extents.x,bounds.extents.y,bounds.extents.z));}
    case "animator_state":{var animator=node.GetComponent<Animator>();if(!animator||!animator.runtimeAnimatorController||!animator.isInitialized)return QueryError("unavailable");int layer=(int)arguments[0];if(layer>=animator.layerCount)return QueryError("invalid-layer");var state=animator.GetCurrentAnimatorStateInfo(layer);return QueryValue(new JObject{{"state_hash",state.fullPathHash},{"normalized_time",state.normalizedTime},{"in_transition",animator.IsInTransition(layer)}});}
    case "raycast":{var origin=(JArray)arguments[0];var direction=(JArray)arguments[1];var ray=new Ray(new Vector3((float)origin[0],(float)origin[1],(float)origin[2]),new Vector3((float)direction[0],(float)direction[1],(float)direction[2]));if(!Physics.Raycast(ray,out var hit,(float)arguments[2],Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return QueryValue(new JObject{{"hit",false}});var owner=hit.transform;long id=0;while(owner&&!ids.TryGetValue(owner,out id))owner=owner.parent;if(!owner)return QueryError("unavailable");return QueryValue(new JObject{{"hit",true},{"node",id},{"point",Vector(hit.point)},{"normal",Vector(hit.normal)},{"distance",hit.distance}});}
    default:throw new NotSupportedException("native-query-not-installed:"+name);
   }
  }
  public BatchAck Commit(CommandBatch batch,object prepared){
   RequireOwner();var transaction=prepared as Preparation;if(transaction==null||!ReferenceEquals(transaction,pendingPreparation)||!ReferenceEquals(batch,transaction.Batch)||batch.BaseRevision!=revision)throw new InvalidOperationException("native-preparation-mismatch");
   foreach(var apply in transaction.Apply)apply();if(SceneCommandProtocol.AdvancesRevision(batch))revision=checked(revision+1);
   pendingPreparation=null;return new BatchAck(identity,batch.Tick,batch.Sequence,true,Snapshot,mapping:transaction.Mapping,results:transaction.Results);
  }
  public void CancelPreparation(){RequireOwner();if(pendingPreparation==null)return;foreach(var lease in pendingPreparation.Leases)prefabs.Return(lease);pendingPreparation=null;}
  public void StepDynamicResources(){if(Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("native-resource-main-thread");resourceCommands?.Step();}
  public bool CanDisposeResourceOwner=>resourceCommands==null||resourceCommands.CanDisposeOwner;
  public void RevokeResourceAuthority(long epoch){RequireOwner();resourceCommands?.Revoke(epoch);}
  public void DisposeResourceCommands(){if(Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("native-resource-main-thread");resourceCommands?.Dispose();}
  public void Dispose(){RequireOwner();DisposeResourceCommands();CancelPreparation();foreach(var lease in dynamicNodes.Values.Select(value=>value.Lease).Distinct().ToArray())prefabs.Return(lease);dynamicNodes.Clear();disposed=true;}
 }
}

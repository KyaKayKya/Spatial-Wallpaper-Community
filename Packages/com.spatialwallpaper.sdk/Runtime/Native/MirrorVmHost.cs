using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Security;

namespace SpatialWallpaper.Sdk.Native {
 public enum NativeQueryState {Unknown,Pending,Ready,Failed}
 // Script-thread only. The Unity renderer never calls or waits for this object.
 public sealed class MirrorVmHost:IVmHost,IVmExecutionGate,IDisposable {
  sealed class Reference {
   internal long Id,Parent;internal string Kind;internal JObject State;internal SceneObject Definition;
   internal bool Alive=true;internal ResourceLease Memory;
   internal bool Supports(string kind)=>Kind==kind||kind=="node"&&(Kind=="node"||Kind=="light");
  }
  sealed class Query {
   internal JToken Result;internal ResourceLease Memory;internal string Name,Error;internal JArray Arguments;
  }
  readonly NativeResourceBindings resourceBindings;
  readonly OwnedDomain domain;readonly SessionIdentity identity;readonly IVmHost capabilities;
  readonly Dictionary<long,Reference> references=new Dictionary<long,Reference>();
  readonly Dictionary<string,long> resources=new Dictionary<string,long>(StringComparer.Ordinal);
  readonly Dictionary<int,long[]> scopes=new Dictionary<int,long[]>();
  readonly Dictionary<long,JArray> prefabGraphs=new Dictionary<long,JArray>();
  readonly List<ResourceLease> bindingMemory=new List<ResourceLease>();
  readonly Dictionary<long,Reference> tentative=new Dictionary<long,Reference>();
  readonly Dictionary<long,Query> queries=new Dictionary<long,Query>();
  readonly List<long> tickQueries=new List<long>();
  readonly List<SceneCommand> commands=new List<SceneCommand>();
  readonly Dictionary<string,int> states=new Dictionary<string,int>(StringComparer.Ordinal);
  SceneSnapshot snapshot;CommandBatch pending;VmRuntime runtime;ResourceLease tickMemory;
  long tick,sequence,nextTemporary=-1,nextQuery=1,observedSequence;int writes,nextScope=1;bool inTick,ranStart,ranUpdate;
  long acknowledgedEvent,tickEvent;
  TickInput currentInput;
  public float DeltaTime { get { Live();return currentInput?.DeltaTime??0; } }
  public byte[] Inputs { get { Live();return currentInput?.Inputs??new byte[0]; } }
  public IReadOnlyList<TickEvent> Events { get; private set; }=Array.AsReadOnly(new TickEvent[0]);
  public bool Stopped { get; private set; }
  public MirrorVmHost(OwnedDomain domain,SessionIdentity identity,SceneSnapshot snapshot,IVmHost capabilities=null){
   this.domain=domain??throw new ArgumentNullException(nameof(domain));this.identity=identity??throw new ArgumentNullException(nameof(identity));
   JsonData.Require(domain.Instance==identity.Instance&&domain.Generation==identity.Generation,"mirror-owner");identity.Require(identity);this.capabilities=capabilities;resourceBindings=new NativeResourceBindings(domain,command=>{if(commands.Count!=0)throw new InvalidOperationException("native-resource-exclusive-batch");Queue(command);},(value,kind)=>Get(value,kind).Id);
   try{Install(snapshot);scopes.Add(0,snapshot.Objects.Where(o=>o.IsNode).Select(o=>o.Id).ToArray());}catch{Dispose();throw;}
  }
  ResourceLease Reserve(long bytes){var lease=domain.Ledger.Reserve(new ResourceCost(bytes,0,0,0));lease.Commit();return lease;}
  static long Cost(SceneObject value)=>1024+value.MetadataBytes*4L;
  void Live(){if(Stopped)throw new InvalidOperationException("mirror-stopped");}
  void Writing(){Live();if(!inTick||pending!=null)throw new InvalidOperationException("mirror-no-writable-tick");}
  public void RequireExecution(string callback){
   Writing();if(callback=="Start"){if(ranStart)throw new InvalidOperationException("mirror-start-already-executed");ranStart=true;}
   else if(callback=="Update"){if(ranUpdate)throw new InvalidOperationException("mirror-update-already-executed");ranUpdate=true;}
   else throw new InvalidOperationException("mirror-unknown-callback");
  }
  Reference Create(SceneObject value,long id,long parent){
   var memory=Reserve(Cost(value));
   try{return new Reference{Id=id,Parent=parent,Kind=value.Kind,State=value.State,Definition=value,Memory=memory};}catch{memory.Dispose();throw;}
  }
  void Install(SceneSnapshot next){
   if(next==null)throw new ArgumentNullException(nameof(next));
   foreach(var value in next.Objects){
    if(references.TryGetValue(value.Id,out var reference)){
     JsonData.Require(reference.Kind==value.Kind,"mirror-reference-kind-changed");
     var memory=Reserve(Cost(value));reference.Memory.Dispose();reference.Memory=memory;reference.State=value.State;reference.Parent=value.Parent;reference.Definition=value;
    }else references.Add(value.Id,Create(value,value.Id,value.Parent));
   }
   foreach(var reference in references.Values.Where(r=>next.Find(r.Id)==null).ToArray())Retire(reference);
   snapshot=next;observedSequence=next.SnapshotSequence;
  }
  void Retire(Reference reference){
   reference.Alive=false;reference.Memory.Dispose();references.Remove(reference.Id);
   domain.InvalidateHandles(handle=>ReferenceEquals(handle.Native,reference));
  }
  Reference Get(object value,string kind){
   domain.RequireType(value,kind);var reference=(value as OwnedHandle)?.Native as Reference;
   if(reference==null||!reference.Alive||!reference.Supports(kind)||!references.TryGetValue(reference.Id,out var live)||!ReferenceEquals(live,reference))throw new InvalidOperationException("mirror-stale-reference");
   return reference;
  }
  public object ResolveId(long id,string kind){
   Live();if(!references.TryGetValue(id,out var value)||!value.Alive||!value.Supports(kind))throw new InvalidOperationException("mirror-unresolved-reference");return domain.Register(kind,value);
  }
  public void BindResource(string name,long id){Live();JsonData.Text(new JValue(name),256);if(!references.ContainsKey(id)||resources.ContainsKey(name)||resources.Count>=SceneCommandCatalog.MaxObjects)throw new InvalidOperationException("mirror-resource-binding");var memory=Reserve(1024);try{resources.Add(name,id);bindingMemory.Add(memory);}catch{memory.Dispose();throw;}}
  public void BindRuntime(VmRuntime value){Live();if(runtime!=null||value==null)throw new InvalidOperationException("mirror-runtime-binding");runtime=value;}
  public void BindRootGraph(long[] nodes){
   Live();if(runtime!=null||inTick||pending!=null||nodes==null||nodes.Length>SceneCommandCatalog.MaxObjects||nodes.Distinct().Count()!=nodes.Length)throw new InvalidOperationException("mirror-root-binding");
   foreach(long id in nodes)if(!references.TryGetValue(id,out var reference)||!reference.Supports("node"))throw new InvalidOperationException("mirror-root-node");scopes[0]=(long[])nodes.Clone();
  }
  // Only admitted program graphs, never external CLR types or Unity components.
  public void BindPrefabGraph(long id,JArray nodes){
   Live();if(!references.TryGetValue(id,out var value)||value.Kind!="prefab"||nodes==null||nodes.Count!=value.Definition.Template.Count||prefabGraphs.ContainsKey(id))throw new InvalidOperationException("mirror-prefab-program-layout");
   long bytes=Encoding.UTF8.GetByteCount(nodes.ToString(Formatting.None));JsonData.Require(bytes<=SceneCommandCatalog.MaxSnapshotBytes,"mirror-prefab-program-size");
   var memory=Reserve(checked(bytes*4+1024));try{prefabGraphs.Add(id,(JArray)nodes.DeepClone());bindingMemory.Add(memory);}catch{memory.Dispose();throw;}
  }
  public object Resolve(string type,JToken value,int scope){
   Live();
   if(type=="node"||type=="light"){
    int index=JsonData.Integer(value,0,SceneCommandCatalog.MaxObjects-1);
    if(!scopes.TryGetValue(scope,out var ids)||index>=ids.Length)throw new InvalidOperationException("mirror-unresolved-scope");return ResolveId(ids[index],type);
   }
   string name=JsonData.Text(value);if(!resources.TryGetValue(name,out var id))throw new InvalidOperationException("mirror-unresolved-resource");return ResolveId(id,type);
  }
  public void ObserveSnapshot(SceneSnapshot next){
   Live();if(next==null||next.SnapshotSequence<observedSequence)throw new InvalidOperationException("mirror-snapshot-order");
   if(next.SceneRevision!=snapshot.SceneRevision){Dispose();throw new InvalidOperationException("mirror-scene-revision-conflict");}
   // A frame observed while author fields/writes are in flight is informational.
   // Only the acknowledging snapshot may replace those local values.
   if(inTick||pending!=null){observedSequence=next.SnapshotSequence;return;}Install(next);
  }
  public void BeginTick(TickInput input){
   Live();if(inTick||pending!=null)throw new InvalidOperationException("mirror-ack-required");
   if(input==null||input.Tick!=tick+1||input.Snapshot.SnapshotSequence<snapshot.SnapshotSequence)throw new InvalidOperationException("mirror-tick-order");
   ObserveSnapshot(input.Snapshot);tickMemory=Reserve(SceneCommandCatalog.MaxPayloadBytes*8L+SceneCommandCatalog.MaxCommands*1024L);
   var events=input.Events.Where(e=>e.Id>acknowledgedEvent).ToArray();
   if(events.Length>0&&events[0].Id!=acknowledgedEvent+1){Dispose();throw new InvalidOperationException("mirror-input-event-gap");}
   resourceBindings.Advance(input.Snapshot);currentInput=input;Events=Array.AsReadOnly(events);tickEvent=events.Length==0?acknowledgedEvent:events[events.Length-1].Id;
   commands.Clear();states.Clear();tentative.Clear();tickQueries.Clear();writes=0;ranStart=ranUpdate=false;tick=input.Tick;inTick=true;
  }
  void Queue(SceneCommand command){
   Writing();try{QueueCore(command);}catch{Dispose();throw;}
  }
  void QueueCore(SceneCommand command){
   SceneCommandRules.Check(command);if(++writes>SceneCommandCatalog.MaxWritesPerTick)throw new InvalidOperationException("mirror-write-cost-limit");
   var operation=SceneCommandRules.Operation(command.Operation);
   if(operation.Mode=="state"){
    string key=command.Target+":"+command.Operation;
    if(operation.Shape.StartsWith("named_",StringComparison.Ordinal))key+=":"+(string)command.Arguments[0];
    if(states.TryGetValue(key,out int index)){commands[index]=command;return;}
    if(commands.Count>=SceneCommandCatalog.MaxCommands)throw new InvalidOperationException("mirror-command-limit");states.Add(key,commands.Count);
   }else{states.Clear();if(commands.Count>=SceneCommandCatalog.MaxCommands)throw new InvalidOperationException("mirror-command-limit");}
   commands.Add(command);
  }
  public CommandBatch EndTick(){
   Writing();try{
    if(runtime!=null&&runtime.State!=VmState.Running)throw new InvalidOperationException("mirror-vm-not-running");
    if(commands.Count==0)resourceBindings.Poll();
    pending=new CommandBatch(identity,tick,snapshot.SceneRevision,checked(sequence+1),commands,tickEvent);SceneCommandProtocol.Validate(pending,identity,snapshot,sequence+1,tick,acknowledgedEvent,tickEvent);
    inTick=false;return pending;
   }catch{Dispose();throw;}
  }
  public void ApplyAck(BatchAck ack){
   if(Stopped)return;
   if(pending==null)throw new InvalidOperationException("mirror-unexpected-ack");
   try{
    identity.Require(ack?.Identity);JsonData.Require(ack.Tick==pending.Tick&&ack.Sequence==pending.Sequence,"mirror-ack-order");
    if(!ack.Accepted){Dispose();return;}
    long expectedRevision=checked(pending.BaseRevision+(SceneCommandProtocol.AdvancesRevision(pending)?1:0));
    JsonData.Require(ack.Snapshot!=null&&ack.Snapshot.SceneRevision==expectedRevision&&ack.Snapshot.SnapshotSequence>=observedSequence&&ack.Snapshot.SnapshotSequence>snapshot.SnapshotSequence,"mirror-ack-snapshot");
    JsonData.Require(ack.Mapping.Count==tentative.Count&&ack.Mapping.Keys.All(tentative.ContainsKey)&&ack.Mapping.Values.All(id=>id>0)&&ack.Mapping.Values.Distinct().Count()==ack.Mapping.Count,"mirror-ack-mapping");
    foreach(var item in ack.Mapping){
     var reference=tentative[item.Key];JsonData.Require(!references.ContainsKey(item.Value),"mirror-mapping-collision");
     if(reference.Alive)JsonData.Require(ack.Snapshot.Find(item.Value)?.Kind==reference.Kind,"mirror-mapping-missing-object");
    }
    var results=ack.Results;long resourceResult=resourceBindings.ExpectedResult;JsonData.Require(results.Count==tickQueries.Count+(resourceResult==0?0:1)&&results.Keys.All(id=>id==resourceResult||tickQueries.Contains(id)),"mirror-query-result-set");resourceBindings.Acknowledge(results,ack.Snapshot);
    foreach(var pair in results.Where(pair=>pair.Key>0)){
     var query=queries[pair.Key];SceneCommandRules.CheckQueryResult(query.Name,query.Arguments,pair.Value,ack.Snapshot);
     if((string)pair.Value["status"]=="error")query.Error=(string)pair.Value["error"];else query.Result=pair.Value["value"].DeepClone();
    }
    foreach(var item in ack.Mapping){
     var reference=tentative[item.Key];if(!reference.Alive)continue;
     references.Remove(reference.Id);reference.Id=item.Value;references.Add(reference.Id,reference);
    }
    foreach(var reference in references.Values)if(reference.Parent<0)reference.Parent=ack.Mapping[reference.Parent];
    foreach(int scope in scopes.Keys.ToArray())scopes[scope]=scopes[scope].Select(id=>ack.Mapping.TryGetValue(id,out var mapped)?mapped:id).ToArray();
    Install(ack.Snapshot);sequence=pending.Sequence;acknowledgedEvent=pending.ConsumedEvent;pending=null;tentative.Clear();tickQueries.Clear();commands.Clear();states.Clear();tickMemory.Dispose();tickMemory=null;currentInput=null;Events=Array.AsReadOnly(new TickEvent[0]);
   }catch{Dispose();throw;}
  }
  static JArray Vector(VectorValue value,int count=3)=>count==3?new JArray(value.X,value.Y,value.Z):new JArray(value.X,value.Y,value.Z,value.W);
  static VectorValue ReadVector(JObject state,string name,int count=3){var a=state[name] as JArray;return a==null?(name=="scale"?new VectorValue("vector3",1,1,1):name=="rotation"?new VectorValue("quaternion",0,0,0,1):new VectorValue("vector3",0,0,0)):new VectorValue(count==4?"quaternion":"vector3",(float)a[0],(float)a[1],(float)a[2],count==4?(float)a[3]:0);}
  public VectorValue GetPosition(OwnedHandle node){Live();return ReadVector(Get(node,"node").State,"position");}
  public void SetPosition(OwnedHandle node,VectorValue value){domain.RequireType(value,"vector3");var reference=Get(node,"node");Queue(new SceneCommand("SetPosition",reference.Id,Vector(value)));reference.State["position"]=Vector(value);}
  public void SetRotation(OwnedHandle node,VectorValue value){domain.Check(value);JsonData.Require(value.Type=="quaternion","mirror-quaternion-type");var reference=Get(node,"node");Queue(new SceneCommand("SetRotation",reference.Id,Vector(value,4)));reference.State["rotation"]=Vector(value,4);}
  static VectorValue Multiply(VectorValue a,VectorValue b)=>new VectorValue("quaternion",a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X,a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W,a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z);
  static VectorValue Axis(double degrees,int axis){double radians=degrees*Math.PI/360;float s=(float)Math.Sin(radians);return new VectorValue("quaternion",axis==0?s:0,axis==1?s:0,axis==2?s:0,(float)Math.Cos(radians));}
  public void Rotate(OwnedHandle node,float x,float y,float z){
   domain.Check(x);domain.Check(y);domain.Check(z);var current=ReadVector(Get(node,"node").State,"rotation",4);
   var result=Multiply(current,Multiply(Multiply(Axis(y,1),Axis(x,0)),Axis(z,2)));
   double length=Math.Sqrt(result.X*result.X+result.Y*result.Y+result.Z*result.Z+result.W*result.W);
   SetRotation(node,new VectorValue("quaternion",(float)(result.X/length),(float)(result.Y/length),(float)(result.Z/length),(float)(result.W/length)));
  }
  static VectorValue Transform(Reference reference,VectorValue point){
   var scale=ReadVector(reference.State,"scale");var rotation=ReadVector(reference.State,"rotation",4);var position=ReadVector(reference.State,"position");
   var v=Multiply(Multiply(rotation,new VectorValue("quaternion",point.X*scale.X,point.Y*scale.Y,point.Z*scale.Z)),new VectorValue("quaternion",-rotation.X,-rotation.Y,-rotation.Z,rotation.W));
   return new VectorValue("vector3",position.X+v.X,position.Y+v.Y,position.Z+v.Z);
  }
  public VectorValue GetWorldPosition(OwnedHandle node){
   Live();var reference=Get(node,"node");var point=new VectorValue("vector3",0,0,0);int depth=0;
   while(reference!=null){if(++depth>SceneCommandCatalog.MaxObjects)throw new InvalidOperationException("mirror-parent-cycle");point=Transform(reference,point);reference=reference.Parent==0?null:references[reference.Parent];}return point;
  }
  public void PlayEffect(OwnedHandle sound,float gain){var value=Get(sound,"sound");Queue(new SceneCommand("PlayEffect",value.Id,new JArray(gain)));}
  public OwnedHandle Instantiate(OwnedHandle prefab){
   Writing();try{return InstantiateCore(prefab);}catch{Dispose();throw;}
  }
  OwnedHandle InstantiateCore(OwnedHandle prefab){
   var template=Get(prefab,"prefab");var nodes=template.Definition.Template;
   if(nodes.Count==0||references.Count+nodes.Count>SceneCommandCatalog.MaxObjects||nextTemporary-nodes.Count<int.MinValue)throw new InvalidOperationException("mirror-prefab-limit");
   var ids=new JArray();var map=new Dictionary<long,long>();foreach(var node in nodes){long id=nextTemporary--;ids.Add(id);map.Add(node.Id,id);}
   Queue(new SceneCommand("Instantiate",template.Id,ids));
   try{
    foreach(var node in nodes){long id=map[node.Id];var reference=Create(node,id,node.Parent==0?0:map[node.Parent]);references.Add(id,reference);tentative.Add(id,reference);}
    int scope=checked(nextScope++);scopes.Add(scope,ids.Select(t=>(long)t).ToArray());
    if(prefabGraphs.TryGetValue(template.Id,out var program)){
     if(runtime==null)throw new InvalidOperationException("mirror-prefab-runtime-required");runtime.BindGraph(program,scope);
    }
    return (OwnedHandle)ResolveId((long)ids[0],"node");
   }catch{Dispose();throw;}
  }
  public void Destroy(OwnedHandle node){
   Writing();var value=Get(node,"node");Queue(new SceneCommand("Destroy",value.Id,new JArray()));var dead=new HashSet<long>{value.Id};bool changed;
   do{changed=false;foreach(var reference in references.Values)if(!dead.Contains(reference.Id)&&dead.Contains(reference.Parent))changed|=dead.Add(reference.Id);}while(changed);
   foreach(var scope in scopes.ToArray()){
    var indices=new HashSet<int>(Enumerable.Range(0,scope.Value.Length).Where(i=>dead.Contains(scope.Value[i])));runtime?.DetachNodes(scope.Key,indices);
    if(scope.Key!=0&&scope.Value.All(dead.Contains))scopes.Remove(scope.Key);
   }
   foreach(long id in dead)Retire(references[id]);
  }
  public long BeginQuery(OwnedHandle node,string name,JArray arguments=null){
   Writing();try{return BeginQueryCore(node,name,arguments??new JArray());}catch{Dispose();throw;}
  }
  long BeginQueryCore(OwnedHandle node,string name,JArray arguments){
   var target=Get(node,"node");if(queries.Count>=SceneCommandCatalog.MaxCommands||nextQuery>int.MaxValue)throw new InvalidOperationException("mirror-query-limit");
   long id=nextQuery++;Queue(new SceneCommand("Query",target.Id,new JArray(name,id,arguments.DeepClone())));var memory=Reserve(4096);
   queries.Add(id,new Query{Memory=memory,Name=name,Arguments=(JArray)arguments.DeepClone()});tickQueries.Add(id);return id;
  }
  public NativeQueryState QueryState(long id){Live();if(!queries.TryGetValue(id,out var query))return NativeQueryState.Unknown;return query.Error!=null?NativeQueryState.Failed:query.Result==null?NativeQueryState.Pending:NativeQueryState.Ready;}
  public string QueryError(long id){Live();return queries.TryGetValue(id,out var query)?query.Error:null;}
  public void ReleaseQuery(long id){Live();if(queries.TryGetValue(id,out var query)){if(tickQueries.Contains(id))throw new InvalidOperationException("mirror-query-unacknowledged");query.Memory.Dispose();queries.Remove(id);}}
  public bool TryTakeQuery(long id,out JToken value){Live();value=null;if(!queries.TryGetValue(id,out var query)||query.Result==null)return false;value=query.Result.DeepClone();query.Memory.Dispose();queries.Remove(id);return true;}
  public bool Supports(string member)=>NativeAuthorApi.Supports(member)||SceneCommandRules.Member(member)!=null||SceneCommandRules.MirrorMember(member)||capabilities!=null&&(!RenderingBindings.Supports(member)||NativeCapabilityBindings.IsView(member))&&capabilities.Supports(member);
  Query ReadyQuery(long id){Live();if(!queries.TryGetValue(id,out var query)||query.Result==null||query.Error!=null)throw new InvalidOperationException("native-query-not-ready");return query;}
  object AuthorCall(string action,object[] args){
   if(action.StartsWith("Resource",StringComparison.Ordinal))return resourceBindings.Call(action,args);
   if(action=="AnimatorFloat"||action=="AnimatorBool"||action=="AnimatorTrigger"){
    var node=Get(args[0],"node");var values=new JArray((string)args[1]);if(action=="AnimatorFloat")values.Add((float)args[2]);else if(action=="AnimatorBool")values.Add((bool)args[2]);Queue(new SceneCommand(action,node.Id,values));return null;
   }
   switch(action){
    case "BeginBounds":return (int)BeginQuery((OwnedHandle)args[0],"bounds");
    case "BeginRaycast":return (int)BeginQuery((OwnedHandle)args[0],"raycast",new JArray(Vector((VectorValue)args[1]),Vector((VectorValue)args[2]),(float)args[3]));
    case "BeginAnimatorState":return (int)BeginQuery((OwnedHandle)args[0],"animator_state",new JArray((int)args[1]));
    case "QueryState":return (int)QueryState((int)args[0]);
    case "QueryError":return QueryError((int)args[0]);
    case "QueryRelease":ReleaseQuery((int)args[0]);return null;
   }
   var query=ReadyQuery((int)args[0]);string field=args.Length>1?(string)args[1]:null;
   if(action=="QueryVector"){
    JArray vector;if(query.Name=="bounds"&&(field=="center"||field=="extents")){var bounds=(JArray)query.Result;int offset=field=="center"?0:3;return new VectorValue("vector3",(float)bounds[offset],(float)bounds[offset+1],(float)bounds[offset+2]);}
    if(query.Name!="raycast"||(field!="point"&&field!="normal")||!(bool)query.Result["hit"])throw new InvalidOperationException("native-query-field-unavailable");vector=(JArray)query.Result[field];return new VectorValue("vector3",(float)vector[0],(float)vector[1],(float)vector[2]);
   }
   if(action=="QueryNode"){if(query.Name!="raycast")throw new InvalidOperationException("native-query-field-unavailable");return (bool)query.Result["hit"]?ResolveId((long)query.Result["node"],"node"):null;}
   if(action=="QueryFloat"&&(query.Name=="animator_state"&&field=="normalized_time"||query.Name=="raycast"&&field=="distance"&&(bool)query.Result["hit"]))return (float)query.Result[field];
   if(action=="QueryInt"&&query.Name=="animator_state"&&field=="state_hash")return (int)query.Result[field];
   if(action=="QueryBool"&&(query.Name=="animator_state"&&field=="in_transition"||query.Name=="raycast"&&field=="hit"))return (bool)query.Result[field];
   throw new InvalidOperationException("native-query-field-unavailable");
  }
  public object Call(string member,object receiver,object[] args){
   Writing();if(!Supports(member))throw new InvalidOperationException("missing-native-mirror-binding:"+member);
   domain.Check(receiver);foreach(var argument in args)domain.Check(argument);
   if(NativeAuthorApi.Supports(member)){string action=NativeAuthorApi.Action(member);if(action=="SetActive"){var node=Get(receiver,"node");Queue(new SceneCommand("SetActive",node.Id,new JArray((bool)args[0])));node.State["active"]=(bool)args[0];return null;}return AuthorCall(action,args);}
   if(SceneCommandRules.MirrorMember(member)){
    if(member.Contains("get_localPosition"))return GetPosition((OwnedHandle)receiver);
    if(member.Contains("get_intensity"))return (float)(Get(receiver,"light").State["intensity"]??new JValue(1));
    Rotate((OwnedHandle)receiver,(float)args[0],(float)args[1],(float)args[2]);return null;
   }
   var operation=SceneCommandRules.Member(member);if(operation==null)return capabilities.Call(member,receiver,args);
   switch(operation.Name){
    case "SetPosition":SetPosition((OwnedHandle)receiver,(VectorValue)args[0]);return null;
    case "Instantiate":return Instantiate((OwnedHandle)args[0]);
    case "Destroy":Destroy((OwnedHandle)args[0]);return null;
    case "PlayEffect":PlayEffect((OwnedHandle)args[0],(float)args[1]);return null;
    case "PlayMusic":Queue(new SceneCommand(operation.Name,Get(args[0],"sound").Id,new JArray((float)args[1])));return null;
    case "StopMusic":case "StopEffects":Queue(new SceneCommand(operation.Name,0,new JArray()));return null;
    case "SetLightIntensity":var light=Get(receiver,"light");Queue(new SceneCommand(operation.Name,light.Id,new JArray((float)args[0])));light.State["intensity"]=(float)args[0];return null;
    case "MaterialFloat":case "MaterialColor":
     var material=Get(receiver,"material");var argument=operation.Name=="MaterialColor"?(JToken)Vector((VectorValue)args[1],4):new JValue((float)args[1]);
     Queue(new SceneCommand(operation.Name,material.Id,new JArray((string)args[0],argument)));return null;
    default:throw new InvalidOperationException("uncovered-native-member");
   }
  }
  public void Dispose(){
   if(Stopped)return;Stopped=true;inTick=false;pending=null;
   resourceBindings.Dispose();foreach(var reference in references.Values)reference.Memory.Dispose();references.Clear();foreach(var query in queries.Values)query.Memory.Dispose();queries.Clear();
   tickMemory?.Dispose();tickMemory=null;commands.Clear();states.Clear();tentative.Clear();scopes.Clear();resources.Clear();prefabGraphs.Clear();
   foreach(var memory in bindingMemory)memory.Dispose();bindingMemory.Clear();
   currentInput=null;Events=Array.AsReadOnly(new TickEvent[0]);
   // A conflict cannot leave VM fields/collections running on a divergent state.
   if(runtime!=null)runtime.Stop();else domain.Dispose();
  }
 }
}

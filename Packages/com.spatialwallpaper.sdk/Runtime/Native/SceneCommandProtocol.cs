using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Sdk.Native {
 public sealed class SessionIdentity {
  public Guid Instance { get; }
  public uint Generation { get; }
  public string CatalogDigest { get; }
  public SessionIdentity(Guid instance,uint generation,string digest){
   JsonData.Require(instance!=Guid.Empty&&generation>0&&digest!=null&&digest.Length==64&&digest.All(Uri.IsHexDigit),"scene-session-identity");
   Instance=instance;Generation=generation;CatalogDigest=digest.ToLowerInvariant();
  }
  public void Require(SessionIdentity other){JsonData.Require(other!=null&&other.Instance==Instance&&other.Generation==Generation&&other.CatalogDigest==CatalogDigest&&CatalogDigest==SceneCommandCatalog.Digest,"scene-session-mismatch");}
 }

 // Immutable value metadata. No Unity object or author CLR type crosses this boundary.
 public sealed class SceneObject {
  public long Id { get; }
  public string Kind { get; }
  public long Parent { get; }
  readonly JObject state;
  public JObject State=>(JObject)state.DeepClone();
  public IReadOnlyList<SceneObject> Template { get; }
  public int MetadataBytes { get; }
  public bool IsNode=>Kind=="node"||Kind=="light";
  public bool Supports(string kind)=>kind==Kind||kind=="node"&&IsNode;
  public SceneObject(long id,string kind,long parent=0,JObject state=null,IEnumerable<SceneObject> template=null){
   JsonData.Require(id!=0&&id>=int.MinValue&&id<=int.MaxValue&&parent>=0&&parent<=int.MaxValue,"scene-object-id");
   JsonData.Require(new[]{"node","light","material","sound","prefab","mesh","texture","cubemap"}.Contains(kind),"scene-object-kind");
   Id=id;Kind=kind;Parent=parent;this.state=state==null?new JObject():(JObject)state.DeepClone();
   // Parse also rejects duplicate keys, metadata and non-finite nested values.
   JsonData.Parse(this.state.ToString(Formatting.None));
   foreach(var value in this.state.Descendants())if(value.Type==JTokenType.Float)JsonData.Number(value,double.MinValue,double.MaxValue);
   if(this.state["position"]!=null)JsonData.Vector(this.state["position"],3);
   if(this.state["rotation"]!=null)SceneCommandRules.Quaternion(this.state["rotation"]);
   if(this.state["scale"]!=null)JsonData.Vector(this.state["scale"],3,-10000,10000);
   if(this.state["active"]!=null)JsonData.Require(this.state["active"].Type==JTokenType.Boolean,"scene-active");
   if(this.state["intensity"]!=null)JsonData.Number(this.state["intensity"],0);
   var nodes=(template??Enumerable.Empty<SceneObject>()).Take(SceneCommandCatalog.MaxCommands+1).ToArray();
   JsonData.Require(nodes.Length<=SceneCommandCatalog.MaxCommands&&(kind=="prefab"||nodes.Length==0),"scene-prefab-size");
   var seen=new HashSet<long>();
   foreach(var node in nodes){JsonData.Require(node!=null&&node.Id>0&&node.IsNode&&node.Template.Count==0&&(node.Parent==0||seen.Contains(node.Parent))&&seen.Add(node.Id),"scene-prefab-tree");}
   JsonData.Require(nodes.Length==0||nodes.Count(n=>n.Parent==0)==1,"scene-prefab-root");
   Template=Array.AsReadOnly(nodes);
   long bytes=512+Encoding.UTF8.GetByteCount(this.state.ToString(Formatting.None))+nodes.Sum(node=>(long)node.MetadataBytes);
   JsonData.Require(bytes<=SceneCommandCatalog.MaxSnapshotBytes,"scene-object-metadata-size");MetadataBytes=(int)bytes;
  }
 }

 public sealed class SceneSnapshot {
  public long SceneRevision { get; }
  public long SnapshotSequence { get; }
  public IReadOnlyList<SceneObject> Objects { get; }
  readonly Dictionary<long,SceneObject> byId;
  public int MetadataBytes { get; }
  public SceneSnapshot(long revision,long sequence,IEnumerable<SceneObject> objects){
   JsonData.Require(revision>0&&sequence>0&&objects!=null,"scene-snapshot-header");
   var array=objects.Take(SceneCommandCatalog.MaxObjects+1).ToArray();
   JsonData.Require(array.Length<=SceneCommandCatalog.MaxObjects,"scene-snapshot-count");
   byId=new Dictionary<long,SceneObject>();long bytes=0;
   foreach(var item in array){
    JsonData.Require(item!=null&&item.Id>0&&!byId.ContainsKey(item.Id)&&(item.Parent==0||byId.TryGetValue(item.Parent,out var parent)&&parent.IsNode),"scene-snapshot-tree");
    byId.Add(item.Id,item);bytes+=item.MetadataBytes;
    JsonData.Require(bytes<=SceneCommandCatalog.MaxSnapshotBytes,"scene-snapshot-bytes");
   }
   SceneRevision=revision;SnapshotSequence=sequence;Objects=Array.AsReadOnly(array);MetadataBytes=(int)bytes;
  }
  public SceneObject Find(long id)=>byId.TryGetValue(id,out var value)?value:null;
 }

 public sealed class SceneCommand {
  public string Operation { get; }
  public long Target { get; }
  readonly JArray arguments;
  public JArray Arguments=>(JArray)arguments.DeepClone();
  public SceneCommand(string operation,long target,JArray args){Operation=operation;Target=target;arguments=args==null?throw new ArgumentNullException(nameof(args)):(JArray)args.DeepClone();}
 }
 public sealed class CommandBatch {
  public SessionIdentity Identity { get; }
  public long Tick { get; }
  public long BaseRevision { get; }
  public long Sequence { get; }
  public long ConsumedEvent { get; }
  public IReadOnlyList<SceneCommand> Commands { get; }
  public CommandBatch(SessionIdentity identity,long tick,long revision,long sequence,IEnumerable<SceneCommand> commands,long consumedEvent=0){
   JsonData.Require(identity!=null&&tick>0&&revision>0&&sequence>0&&commands!=null&&consumedEvent>=0,"scene-batch-header");
   Identity=identity;Tick=tick;BaseRevision=revision;Sequence=sequence;
   ConsumedEvent=consumedEvent;
   var array=commands.Take(SceneCommandCatalog.MaxCommands+1).ToArray();
   JsonData.Require(array.Length<=SceneCommandCatalog.MaxCommands&&array.All(c=>c!=null),"scene-command-count");Commands=Array.AsReadOnly(array);
  }
 }
 public sealed class TickEvent {
  public long Id { get; }
  public string Kind { get; }
  public string Button { get; }
  public float Value { get; }
  public TickEvent(long id,string kind,string button=null,float value=0){
   JsonData.Require(id>0&&(kind=="down"||kind=="up"||kind=="wheel"),"tick-event-kind");
   JsonData.Require(kind=="wheel"?button==null:new[]{"Primary","Secondary","Space","Left","Right","Up","Down"}.Contains(button),"tick-event-button");
   JsonData.Number(new JValue(value),-100000,100000);JsonData.Require(kind=="wheel"||value==0,"tick-event-value");Id=id;Kind=kind;Button=button;Value=value;
  }
 }
 public sealed class TickInput {
  public NativeViewSnapshot View { get; }
  public long Tick { get; }
  public SceneSnapshot Snapshot { get; }
  public float DeltaTime { get; }
  readonly byte[] inputs;
  // Existing authenticated InputSnapshotCodec payload; never system input handles.
  public byte[] Inputs=>(byte[])inputs.Clone();
  public IReadOnlyList<TickEvent> Events { get; }
  public TickInput(long tick,SceneSnapshot snapshot,float deltaTime=1f/60,byte[] inputs=null,IEnumerable<TickEvent> events=null,NativeViewSnapshot view=null){
   View=view;
   JsonData.Number(new JValue(deltaTime),0,.25);JsonData.Require(tick>0&&(inputs==null||inputs.Length==Capabilities.InputSnapshotCodec.Bytes),"tick-input-fields");
   if(inputs!=null)Capabilities.InputSnapshotCodec.RequireShape(inputs);
   Tick=tick;Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));DeltaTime=deltaTime;this.inputs=inputs==null?new byte[0]:(byte[])inputs.Clone();
   var array=(events??Enumerable.Empty<TickEvent>()).Take(SceneCommandCatalog.MaxCommands+1).ToArray();
   JsonData.Require(array.Length<=SceneCommandCatalog.MaxCommands&&array.All(e=>e!=null),"tick-event-count");
   for(int i=1;i<array.Length;i++)JsonData.Require(array[i].Id==array[i-1].Id+1,"tick-event-order");Events=Array.AsReadOnly(array);
  }
 }
 public sealed class BatchAck {
  public SessionIdentity Identity { get; }
  public long Tick { get; }
  public long Sequence { get; }
  public bool Accepted { get; }
  public SceneSnapshot Snapshot { get; }
  public IReadOnlyDictionary<long,long> Mapping { get; }
  readonly Dictionary<long,JToken> results;
  public IReadOnlyDictionary<long,JToken> Results=>new ReadOnlyDictionary<long,JToken>(results.ToDictionary(p=>p.Key,p=>p.Value.DeepClone()));
  public BatchAck(SessionIdentity identity,long tick,long sequence,bool accepted,SceneSnapshot snapshot,IDictionary<long,long> mapping=null,IDictionary<long,JToken> results=null){
   Identity=identity;Tick=tick;Sequence=sequence;Accepted=accepted;Snapshot=snapshot;
   Mapping=new ReadOnlyDictionary<long,long>(new Dictionary<long,long>(mapping??new Dictionary<long,long>()));
   this.results=(results??new Dictionary<long,JToken>()).ToDictionary(p=>p.Key,p=>p.Value.DeepClone());
  }
 }

 public sealed class SceneOperation {
  public int Id { get; }
  public string Name { get; }
  public string Kind { get; }
  public string Shape { get; }
  public string Mode { get; }
  public string Member { get; }
  internal SceneOperation(JObject value){Id=(int)value["id"];Name=(string)value["name"];Kind=(string)value["kind"];Shape=(string)value["shape"];Mode=(string)value["mode"];Member=(string)value["member"];}
 }
 public static class SceneCommandRules {
  static readonly JObject source=JsonData.Parse(SceneCommandCatalog.Json);
  static readonly SceneOperation[] operations=((JArray)source["operations"]).Cast<JObject>().Select(o=>new SceneOperation(o)).ToArray();
  public static SceneOperation Operation(string name){var op=operations.SingleOrDefault(o=>o.Name==name);JsonData.Require(op!=null,"unknown-scene-operation");return op;}
  public static SceneOperation Operation(int id){var op=operations.SingleOrDefault(o=>o.Id==id);JsonData.Require(op!=null,"unknown-scene-operation");return op;}
  public static SceneOperation Member(string member)=>operations.SingleOrDefault(o=>o.Member==member);
  public static bool MirrorMember(string member)=>((JArray)source["mirror_members"]).Any(t=>(string)t==member);
  static JObject Query(string name){var query=((JArray)source["queries"]).Cast<JObject>().SingleOrDefault(q=>(string)q["name"]==name);JsonData.Require(query!=null,"unknown-native-query");return query;}
  public static void CheckQueryArguments(string name,JArray args){
   JsonData.Require(args!=null,"native-query-arguments");string shape=(string)Query(name)["arguments"];
   switch(shape){
    case "empty":JsonData.Require(args.Count==0,"native-query-empty");break;
    case "layer":JsonData.Require(args.Count==1,"native-query-layer");JsonData.Integer(args[0],0,31);break;
    case "ray":
     JsonData.Require(args.Count==3,"native-query-ray");JsonData.Vector(args[0],3);JsonData.Vector(args[1],3,-1,1);JsonData.Number(args[2],0);
     JsonData.Require(Math.Abs(((JArray)args[1]).Sum(v=>(double)v*(double)v)-1)<.001,"native-query-ray-direction");break;
    default:throw new InvalidDataException("native-query-argument-shape");
   }
  }
  public static void CheckQueryResult(string name,JArray arguments,JToken token,SceneSnapshot snapshot){
   var reply=token as JObject;JsonData.Require(reply!=null,"native-query-reply");string status=JsonData.Text(reply["status"],32);
   if(status=="error"){JsonData.Fields(reply,"status","error");JsonData.Require(new[]{"unavailable","destroyed","invalid-layer","cancelled"}.Contains(JsonData.Text(reply["error"],32)),"native-query-error");return;}
   JsonData.Fields(reply,"status","value");JsonData.Require(status=="ok","native-query-status");var value=reply["value"];
   switch((string)Query(name)["result"]){
    case "bounds6":JsonData.Vector(value,6);for(int i=3;i<6;i++)JsonData.Number(value[i],0);break;
    case "animator":var animator=value as JObject;JsonData.Fields(animator,"state_hash","normalized_time","in_transition");JsonData.Integer(animator["state_hash"],int.MinValue,int.MaxValue);JsonData.Number(animator["normalized_time"],-1000000,1000000);JsonData.Require(animator["in_transition"]?.Type==JTokenType.Boolean,"native-query-animator-transition");break;
    case "hit":
     var hit=value as JObject;JsonData.Require(hit?["hit"]?.Type==JTokenType.Boolean,"native-query-hit");
     if(!(bool)hit["hit"]){JsonData.Fields(hit,"hit");break;}
     JsonData.Fields(hit,"hit","node","point","normal","distance");JsonData.Require(snapshot.Find(Id(hit["node"]))?.IsNode==true,"native-query-foreign-hit");
     JsonData.Vector(hit["point"],3);JsonData.Vector(hit["normal"],3,-1,1);JsonData.Number(hit["distance"],0,(double)arguments[2]);
     JsonData.Require(Math.Abs(((JArray)hit["normal"]).Sum(v=>(double)v*(double)v)-1)<.001,"native-query-hit-normal");break;
    default:throw new InvalidDataException("native-query-result-shape");
   }
  }
  internal static long Id(JToken token,bool negative=false){JsonData.Require(token?.Type==JTokenType.Integer,"scene-reference-type");long id=(long)token;JsonData.Require(negative?id>=int.MinValue&&id<0:id>=int.MinValue&&id<=int.MaxValue,"scene-reference-range");return id;}
  internal static void Quaternion(JToken token){JsonData.Vector(token,4,-1,1);JsonData.Require(Math.Abs(((JArray)token).Sum(v=>(double)v*(double)v)-1)<.001,"scene-quaternion-normalization");}
  public static void Check(SceneCommand command){
   var op=Operation(command.Operation);var args=command.Arguments;
   JsonData.Require(command.Target>=int.MinValue&&command.Target<=int.MaxValue&&(op.Kind=="none"?command.Target==0:command.Target!=0),"scene-target");
   switch(op.Shape){
    case "vector3":JsonData.Vector(args,3);break;
    case "scale3":JsonData.Vector(args,3,-10000,10000);break;
    case "quaternion":Quaternion(args);break;
    case "bool":JsonData.Require(args.Count==1&&args[0].Type==JTokenType.Boolean,"scene-bool");break;
    case "nonnegative":case "gain":JsonData.Require(args.Count==1,"scene-number-count");JsonData.Number(args[0],0,op.Shape=="gain"?16:100000);break;
    case "empty":JsonData.Require(args.Count==0,"scene-empty-arguments");break;
    case "name":JsonData.Require(args.Count==1,"scene-name-count");JsonData.Text(args[0],128);break;
    case "named_float":case "named_color":case "named_bool":
     JsonData.Require(args.Count==2,"scene-named-arguments");JsonData.Text(args[0],128);
     if(op.Shape=="named_float")JsonData.Number(args[1]);else if(op.Shape=="named_color")JsonData.Vector(args[1],4);else JsonData.Require(args[1].Type==JTokenType.Boolean,"scene-bool");break;
    case "temporary_ids":JsonData.Require(args.Count>0&&args.Count<=SceneCommandCatalog.MaxCommands,"scene-temporary-count");foreach(var id in args)Id(id,true);break;
    case "resource":NativeResourceRules.Check(args);break;
    case "query":JsonData.Require(args.Count==3&&Id(args[1])>0,"scene-query");CheckQueryArguments(JsonData.Text(args[0],32),args[2] as JArray);break;
    default:throw new InvalidDataException("scene-rule-shape");
   }
  }
 }

 public static class SceneCommandProtocol {
  public const int HeaderBytes=88;
  const uint Magic=0x4e435753;
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  public static bool AdvancesRevision(CommandBatch batch)=>batch.Commands.Any(c=>{string mode=SceneCommandRules.Operation(c.Operation).Mode;if(c.Operation=="Resource")return (string)c.Arguments[2]=="apply"||(string)c.Arguments[2]=="release";return mode=="state"||mode=="resource";});
  public static byte[] Encode(CommandBatch batch){
   batch.Identity.Require(batch.Identity);
   var commands=new JArray();foreach(var command in batch.Commands){SceneCommandRules.Check(command);commands.Add(new JObject{{"op",SceneCommandRules.Operation(command.Operation).Id},{"target",command.Target},{"args",command.Arguments}});}
   byte[] payload=Utf8.GetBytes(new JObject{{"commands",commands},{"consumed_event",batch.ConsumedEvent}}.ToString(Formatting.None));
   JsonData.Require(payload.Length<=SceneCommandCatalog.MaxPayloadBytes,"scene-payload-size");
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Utf8)){
    writer.Write(Magic);writer.Write((ushort)SceneCommandCatalog.Abi);writer.Write((ushort)0);writer.Write(batch.Identity.Instance.ToByteArray());writer.Write(batch.Identity.Generation);
    for(int i=0;i<32;i++)writer.Write(Convert.ToByte(batch.Identity.CatalogDigest.Substring(i*2,2),16));
    writer.Write(batch.Tick);writer.Write(batch.BaseRevision);writer.Write(batch.Sequence);writer.Write(payload.Length);writer.Write(payload);return stream.ToArray();
   }
  }
  public static CommandBatch Decode(byte[] packet,SessionIdentity expected){
   JsonData.Require(packet!=null&&packet.Length>=HeaderBytes&&packet.Length<=HeaderBytes+SceneCommandCatalog.MaxPayloadBytes,"scene-packet-size");
   using(var stream=new MemoryStream(packet,false))using(var reader=new BinaryReader(stream,Utf8)){
    JsonData.Require(reader.ReadUInt32()==Magic&&reader.ReadUInt16()==SceneCommandCatalog.Abi&&reader.ReadUInt16()==0,"scene-packet-abi");
    var identity=new SessionIdentity(new Guid(reader.ReadBytes(16)),reader.ReadUInt32(),BitConverter.ToString(reader.ReadBytes(32)).Replace("-","").ToLowerInvariant());expected.Require(identity);
    long tick=reader.ReadInt64(),revision=reader.ReadInt64(),sequence=reader.ReadInt64();int length=reader.ReadInt32();
    JsonData.Require(length>=0&&length<=SceneCommandCatalog.MaxPayloadBytes&&packet.Length==HeaderBytes+length,"scene-packet-length");
    var root=JsonData.Parse(Utf8.GetString(reader.ReadBytes(length)));JsonData.Fields(root,"commands","consumed_event");var commands=new List<SceneCommand>();
    JsonData.Require(root["consumed_event"]?.Type==JTokenType.Integer,"scene-event-watermark");long consumed=(long)root["consumed_event"];JsonData.Require(consumed>=0,"scene-event-watermark");
    foreach(var token in JsonData.Array(root["commands"],SceneCommandCatalog.MaxCommands)){
     var value=token as JObject;JsonData.Fields(value,"op","target","args");var op=SceneCommandRules.Operation(JsonData.Integer(value["op"],1,int.MaxValue));
     var command=new SceneCommand(op.Name,SceneCommandRules.Id(value["target"]),JsonData.Array(value["args"],SceneCommandCatalog.MaxCommands));SceneCommandRules.Check(command);commands.Add(command);
    }
    return new CommandBatch(identity,tick,revision,sequence,commands,consumed);
   }
  }
  // Pure preflight stages reference changes in a private table. No native side effects.
  public static void Validate(CommandBatch batch,SessionIdentity identity,SceneSnapshot snapshot,long sequence,long tick,long previousConsumedEvent=0,long publishedEvent=0){
   identity.Require(batch.Identity);JsonData.Require(batch.Sequence==sequence&&batch.Tick==tick,"scene-batch-order");
   JsonData.Require(previousConsumedEvent>=0&&publishedEvent>=previousConsumedEvent&&batch.ConsumedEvent>=previousConsumedEvent&&batch.ConsumedEvent<=publishedEvent,"scene-event-watermark");
   JsonData.Require(batch.BaseRevision==snapshot.SceneRevision,"scene-revision-conflict");Encode(batch);
   var table=snapshot.Objects.ToDictionary(o=>o.Id);var used=new HashSet<long>(table.Keys);var queries=new HashSet<long>();var stagedParents=new Dictionary<long,long>();long metadata=snapshot.MetadataBytes;
   foreach(var command in batch.Commands){
    var op=SceneCommandRules.Operation(command.Operation);SceneObject target=null;
    JsonData.Require(op.Kind=="none"||table.TryGetValue(command.Target,out target)&&target.Supports(op.Kind),"unknown-or-stale-scene-reference");
    if(op.Name=="Resource"){JsonData.Require(batch.Commands.Count==1,"native-resource-exclusive-batch");long id=(long)command.Arguments[3];JsonData.Require(table.TryGetValue(id,out var bound)&&bound.Supports((string)command.Arguments[2]=="begin_texture"?"material":(string)command.Arguments[2]=="begin_mesh"?"node":table[id].Kind),"native-resource-target");}
    if(op.Name=="Instantiate"){
     var ids=command.Arguments;JsonData.Require(target.Template.Count>0&&ids.Count==target.Template.Count,"scene-prefab-layout");
     JsonData.Require(table.Count+ids.Count<=SceneCommandCatalog.MaxObjects,"scene-object-count");
     metadata+=target.Template.Sum(node=>(long)node.MetadataBytes);JsonData.Require(metadata<=SceneCommandCatalog.MaxSnapshotBytes,"scene-instantiated-metadata-size");
     var mapped=new Dictionary<long,long>();
     for(int i=0;i<ids.Count;i++){
      long id=(long)ids[i];var node=target.Template[i];JsonData.Require(id<0&&used.Add(id),"duplicate-scene-temporary");mapped.Add(node.Id,id);
      // Parent links in the staged table may be temporary; retain them separately.
      table.Add(id,new SceneObject(id,node.Kind,0,node.State));
     }
     for(int i=0;i<ids.Count;i++)stagedParents[(long)ids[i]]=target.Template[i].Parent==0?0:mapped[target.Template[i].Parent];
    }else if(op.Name=="Destroy"){
     var dead=new HashSet<long>{target.Id};bool changed;
     do{changed=false;foreach(var item in table.Values)if(!dead.Contains(item.Id)&&dead.Contains(stagedParents.TryGetValue(item.Id,out var parent)?parent:item.Parent))changed|=dead.Add(item.Id);}while(changed);
     foreach(long id in dead){metadata-=table[id].MetadataBytes;table.Remove(id);}
    }else if(op.Name=="Query")JsonData.Require(queries.Add((long)command.Arguments[1]),"duplicate-scene-query");
   }
  }
 }
}

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Sdk.Native {
 public sealed class ScriptMessage {
  public string Type { get; }
  readonly JObject payload;
  public JObject Payload=>(JObject)payload.DeepClone();
  internal ScriptMessage(string type,JObject payload){Type=type;this.payload=payload;}
 }
 // Anonymous inherited pipes only. The native side limits untrusted replies to
 // 128KiB; only the trusted parent may send larger metadata-only snapshots.
 public static class ScriptWire {
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  static int Limit(bool toWorker)=>toWorker?SceneCommandCatalog.MaxSnapshotBytes+SceneCommandCatalog.MaxScriptBytes+65536:131072;
  static void Type(string type,bool toWorker){JsonData.Require((toWorker?new[]{"start","tick","ack","stop"}:new[]{"hello","batch","fault"}).Contains(type),"script-message-direction");}
  public static void Write(Stream stream,SessionIdentity identity,string type,JObject payload,bool toWorker){
   identity.Require(identity);Type(type,toWorker);
   var root=new JObject{{"version",1},{"session",identity.Instance.ToString("N")},{"generation",identity.Generation},{"catalog",identity.CatalogDigest},{"type",type},{"payload",payload.DeepClone()}};
   byte[] data=Utf8.GetBytes(root.ToString(Formatting.None));JsonData.Require(data.Length<=Limit(toWorker),"script-message-size");
   byte[] header=BitConverter.GetBytes(data.Length);stream.Write(header,0,4);stream.Write(data,0,data.Length);stream.Flush();
  }
  static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){int count=stream.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException("script-pipe-eof");at+=count;}}
  public static ScriptMessage Read(Stream stream,SessionIdentity identity,bool toWorker){
   byte[] header=new byte[4];Exact(stream,header);int length=BitConverter.ToInt32(header,0);JsonData.Require(length>0&&length<=Limit(toWorker),"script-message-size");
   byte[] data=new byte[length];Exact(stream,data);var root=JsonData.Parse(Utf8.GetString(data));JsonData.Fields(root,"version","session","generation","catalog","type","payload");
   JsonData.Integer(root["version"],1,1);JsonData.Require(root["generation"]?.Type==JTokenType.Integer,"script-message-generation");
   long generation=(long)root["generation"];JsonData.Require(generation>0&&generation<=uint.MaxValue,"script-message-generation");
   Guid instance;JsonData.Require(Guid.TryParseExact(JsonData.Text(root["session"],32),"N",out instance),"script-message-session");
   identity.Require(new SessionIdentity(instance,(uint)generation,JsonData.Text(root["catalog"],64)));
   string type=JsonData.Text(root["type"],16);Type(type,toWorker);JsonData.Require(root["payload"] is JObject,"script-message-payload");return new ScriptMessage(type,(JObject)root["payload"]);
  }
  static long Positive(JToken token){JsonData.Require(token?.Type==JTokenType.Integer,"script-positive-integer");long value=(long)token;JsonData.Require(value>0,"script-positive-integer");return value;}
  static JObject ObjectValue(SceneObject value)=>new JObject{{"id",value.Id},{"kind",value.Kind},{"parent",value.Parent},{"state",value.State},{"template",new JArray(value.Template.Select(ObjectValue))}};
  public static JObject Snapshot(SceneSnapshot snapshot)=>new JObject{{"revision",snapshot.SceneRevision},{"sequence",snapshot.SnapshotSequence},{"objects",new JArray(snapshot.Objects.Select(ObjectValue))}};
  static SceneObject ObjectValue(JToken token,int depth=0){
   JsonData.Require(depth<2,"script-template-depth");var value=token as JObject;JsonData.Fields(value,"id","kind","parent","state","template");JsonData.Require(value["state"] is JObject,"script-object-state");
   long id=SceneCommandRules.Id(value["id"]),parent=SceneCommandRules.Id(value["parent"]);JsonData.Require(id>0&&parent>=0,"script-object-reference");
   return new SceneObject(id,JsonData.Text(value["kind"],16),parent,(JObject)value["state"],JsonData.Array(value["template"],SceneCommandCatalog.MaxCommands).Select(t=>ObjectValue(t,depth+1)));
  }
  public static SceneSnapshot Snapshot(JObject value){
   JsonData.Fields(value,"revision","sequence","objects");return new SceneSnapshot(Positive(value["revision"]),Positive(value["sequence"]),JsonData.Array(value["objects"],SceneCommandCatalog.MaxObjects).Select(t=>ObjectValue(t)));
  }
  public static JObject Tick(TickInput input)=>new JObject{{"tick",input.Tick},{"dt",input.DeltaTime},{"snapshot",Snapshot(input.Snapshot)},
   {"inputs",input.Inputs.Length==0?JValue.CreateNull():new JValue(Convert.ToBase64String(input.Inputs))},
   {"view",input.View==null?(JToken)JValue.CreateNull():new JArray(input.View.EyeX,input.View.EyeY,input.View.EyeZ,input.View.Width,input.View.Height)},
   {"events",new JArray(input.Events.Select(e=>new JObject{{"id",e.Id},{"kind",e.Kind},{"button",e.Button},{"value",e.Value}}))}};
  public static TickInput Tick(JObject value){
   JsonData.Fields(value,"tick","dt","snapshot","inputs","events","view");
   NativeViewSnapshot view=null;if(value["view"].Type!=JTokenType.Null){var v=JsonData.Array(value["view"],5);JsonData.Require(v.Count==5,"native-view-shape");view=new NativeViewSnapshot((float)JsonData.Number(v[0]),(float)JsonData.Number(v[1]),(float)JsonData.Number(v[2]),(float)JsonData.Number(v[3]),(float)JsonData.Number(v[4]));}byte[] inputs=null;if(value["inputs"].Type!=JTokenType.Null)inputs=Convert.FromBase64String(JsonData.Text(value["inputs"],128));
   var events=new List<TickEvent>();foreach(JObject item in JsonData.Array(value["events"],SceneCommandCatalog.MaxCommands)){
    JsonData.Fields(item,"id","kind","button","value");string button=item["button"].Type==JTokenType.Null?null:JsonData.Text(item["button"],16);
    events.Add(new TickEvent(Positive(item["id"]),JsonData.Text(item["kind"],16),button,(float)JsonData.Number(item["value"])));
   }
   return new TickInput(Positive(value["tick"]),Snapshot(value["snapshot"] as JObject),(float)JsonData.Number(value["dt"],0,.25),inputs,events,view);
  }
  public static JObject Batch(CommandBatch batch)=>new JObject{{"data",Convert.ToBase64String(SceneCommandProtocol.Encode(batch))}};
  public static CommandBatch Batch(JObject value,SessionIdentity identity){
   JsonData.Fields(value,"data");int limit=((SceneCommandProtocol.HeaderBytes+SceneCommandCatalog.MaxPayloadBytes+2)/3)*4;
   return SceneCommandProtocol.Decode(Convert.FromBase64String(JsonData.Text(value["data"],limit)),identity);
  }
  public static JObject Ack(BatchAck ack){
   var mapping=new JObject();foreach(var pair in ack.Mapping)mapping.Add(pair.Key.ToString(CultureInfo.InvariantCulture),pair.Value);
   var results=new JObject();foreach(var pair in ack.Results)results.Add(pair.Key.ToString(CultureInfo.InvariantCulture),pair.Value);
   return new JObject{{"tick",ack.Tick},{"sequence",ack.Sequence},{"accepted",ack.Accepted},{"snapshot",Snapshot(ack.Snapshot)},{"mapping",mapping},{"results",results}};
  }
  static long Key(string text,bool negative){long id;JsonData.Require(long.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out id)&&id.ToString(CultureInfo.InvariantCulture)==text&&(negative?id>=int.MinValue&&id<0:id>0&&id<=int.MaxValue),"script-result-id");return id;}
  public static BatchAck Ack(JObject value,SessionIdentity identity){
   JsonData.Fields(value,"tick","sequence","accepted","snapshot","mapping","results");JsonData.Require(value["accepted"]?.Type==JTokenType.Boolean,"script-ack-accepted");
   var mappings=value["mapping"] as JObject;var results=value["results"] as JObject;
   JsonData.Require(mappings!=null&&results!=null&&mappings.Count<=SceneCommandCatalog.MaxObjects&&results.Count<=SceneCommandCatalog.MaxCommands,"script-ack-size");
   var map=new Dictionary<long,long>();foreach(var pair in mappings.Properties()){long id=SceneCommandRules.Id(pair.Value);JsonData.Require(id>0,"script-mapping-positive");map.Add(Key(pair.Name,true),id);}
   return new BatchAck(identity,Positive(value["tick"]),Positive(value["sequence"]),(bool)value["accepted"],Snapshot(value["snapshot"] as JObject),map,results.Properties().ToDictionary(p=>Key(p.Name,p.Name.StartsWith("-",StringComparison.Ordinal)),p=>p.Value));
  }
 }
}

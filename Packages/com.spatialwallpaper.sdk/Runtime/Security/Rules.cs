using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Security {
 public sealed class RuleCatalog {
  readonly JObject data;readonly string digest;
  readonly Dictionary<string,JObject> members;
  public static RuleCatalog Current {get;}=new RuleCatalog();
  RuleCatalog(JObject source=null,string hash=null){data=source??JObject.Parse(RuleSource.Json);digest=hash??RuleSource.Digest;members=((JArray)data["members"]).Cast<JObject>().ToDictionary(x=>(string)x["id"],StringComparer.Ordinal);}
  public static RuleCatalog CreateNative(JObject native,string catalogDigest){
   var value=Current.Snapshot;value["id"]="spatial-native-rules-1";value["sdk"]=native["sdk_target"].DeepClone();value["schema"]=native["schema_target"].DeepClone();value["base_rules_sha256"]=Current.Digest;value["scene_commands_sha256"]=catalogDigest;
   foreach(JObject entry in (JArray)native["author_members"]){string returns=(string)entry["returns"],receiver=(string)entry["receiver"]??"";var arguments=(JArray)entry["arguments"];var constraints=new JArray();foreach(string argument in arguments){var constraint=Current.Constraint(argument);constraint["nullable"]=false;constraints.Add(constraint);}((JArray)value["members"]).Add(new JObject{{"id",entry["id"].DeepClone()},{"arguments",arguments.DeepClone()},{"returns",returns},{"receiver",receiver},{"receiver_constraint",Current.Constraint(receiver==""?"void":receiver)},{"argument_constraints",constraints},{"return_constraint",Current.Constraint(returns)},{"capability",""},{"cost",4},{"cost_status","candidate-unmeasured"}});}
   return new RuleCatalog(value,SpatialWallpaper.Content.PackageValidator.Identity(value));
  }
  public string Id=>(string)data["id"];
  public string Digest=>digest;
  public JObject Snapshot=>(JObject)data.DeepClone();
  public JObject Member(string id){Require(members.ContainsKey(id),"member-not-allowed:"+id);return (JObject)members[id].DeepClone();}
  internal ShaderBundleGate.ShaderDescription BuiltinShader(string name){
   var shader=data["builtin_shaders"][name] as JObject;
   Require(shader!=null,"material-shader");var result=new ShaderBundleGate.ShaderDescription{Name=name};
   foreach(var property in shader.Properties()){result.Properties.Add(property.Name,(int)property.Value["type"]);result.TextureDimensions.Add(property.Name,(int)property.Value["dimension"]);}
   return result;
  }
  public string ListElement(string type)=>(string)data["list_types"][type];
  public string[] ContentCapabilities(string category,string kind){var tags=data["content_capabilities"]?[category]?[kind] as JArray;return tags==null?new string[0]:tags.Select(tag=>(string)tag).ToArray();}
  public int Limit(string name)=>(int)data["limits"][name];
  public JObject Constraint(string type){
   if(type.EndsWith("[]",StringComparison.Ordinal)){var array=(JObject)data["array_constraint"].DeepClone();array["type"]=type;return array;}
   var constraint=data["value_constraints"][type] as JObject;return constraint!=null?(JObject)constraint.DeepClone():new JObject{{"type",type},{"nullable",true},{"ownership","instance-and-generation"}};
  }
  public bool IsType(string id)=>((JArray)data["types"]).Any(x=>(string)x==id);
  public string Operand(string op){Require(((JObject)data["instructions"]).ContainsKey(op),"opcode-not-allowed:"+op);return (string)data["instructions"][op];}
  internal static void Require(bool yes,string code){JsonData.Require(yes,code);}
 }
}

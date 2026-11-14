using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk {
 public static class JsonData {
  public static void Require(bool valid,string message){if(!valid)throw new InvalidDataException(message);}
  public static JObject Parse(string text){
   try {
    Require(text!=null&&text.Length<=32*1024*1024,"json-size");
    using(var input=new JsonTextReader(new StringReader(text)){MaxDepth=32,DateParseHandling=DateParseHandling.None}){
     var value=JObject.Load(input,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,CommentHandling=CommentHandling.Load});
     Require(!input.Read(),"json-trailing-data");
     foreach(var token in value.DescendantsAndSelf()){
      Require(token.Type!=JTokenType.Comment&&token.Type!=JTokenType.Undefined,"json-token");
      if(token.Type==JTokenType.Float){double v=(double)token;Require(!double.IsNaN(v)&&!double.IsInfinity(v),"non-finite-number");}
      if(token.Type==JTokenType.Property)Require(!((JProperty)token).Name.StartsWith("$",StringComparison.Ordinal),"type-metadata-forbidden");
     }
     return value;
    }
   }catch(JsonException e){throw new InvalidDataException("invalid-json",e);}
  }
  public static void Fields(JObject value,params string[] fields){Require(value!=null&&value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n=>n,StringComparer.Ordinal)),"unexpected-or-missing-fields");}
  public static string Text(JToken token,int max=256){Require(token?.Type==JTokenType.String,"expected-string");string v=(string)token;Require(v.Length<=max,"string-size");return v;}
  public static double Number(JToken token,double min=-100000,double max=100000){Require(token!=null&&(token.Type==JTokenType.Float||token.Type==JTokenType.Integer),"expected-number");double v=(double)token;Require(!double.IsNaN(v)&&!double.IsInfinity(v)&&v>=min&&v<=max,"number-range");return v;}
  public static int Integer(JToken token,int min,int max){Require(token?.Type==JTokenType.Integer,"expected-integer");return (int)Number(token,min,max);}
  public static void Vector(JToken token,int count,double min=-100000,double max=100000){Require(token is JArray a&&a.Count==count,"vector-size");foreach(var v in (JArray)token)Number(v,min,max);}
  public static JArray Array(JToken token,int max){Require(token is JArray a&&a.Count<=max,"array-size");return (JArray)token;}
 }
 public static class SceneData {
  public static JObject Validate(JObject value){
   if(value.ContainsKey("effects"))JsonData.Fields(value,"frame","nodes","meshes","textures","materials","lightmaps","lightmapMode","environment","effects");else JsonData.Fields(value,"frame","nodes","meshes","textures","materials","lightmaps","lightmapMode","environment");
   JsonData.Vector(value["frame"],3,.001,10000);
   var meshes=ResourceList(value["meshes"]);
   var textures=ResourceList(value["textures"]);
   var materials=JsonData.Array(value["materials"],4096);
   foreach(var token in materials){
    var m=token as JObject;
    JsonData.Fields(m,"shader","bundle","properties","keywords","queue","instancing");
    JsonData.Text(m["shader"]);JsonData.Text(m["bundle"]);
    JsonData.Require(m["properties"] is JObject&&((JObject)m["properties"]).Count<=128,"material-properties");
    foreach(var p in ((JObject)m["properties"]).Properties()){
     JsonData.Require(p.Name.Length<=128&&p.Value is JObject,"material-property");var v=(JObject)p.Value;
     JsonData.Fields(v,"type","value");string kind=JsonData.Text(v["type"]);
     switch(kind){case "float":JsonData.Number(v["value"]);break;case "color":case "vector":JsonData.Vector(v["value"],4);break;
      case "texture":JsonData.Integer(v["value"],-1,textures.Count-1);break;default:throw new InvalidDataException("material-property-type");}
    }
    foreach(var keyword in JsonData.Array(m["keywords"],64))JsonData.Text(keyword,128);
    JsonData.Integer(m["queue"],-1,5000);JsonData.Require(m["instancing"]?.Type==JTokenType.Boolean,"instancing-bool");
   }
   var lightmaps=JsonData.Array(value["lightmaps"],256);
   string mode=JsonData.Text(value["lightmapMode"]);JsonData.Require(mode=="non-directional"||mode=="directional","lightmap-mode");
   foreach(var lm in lightmaps){JsonData.Require(lm is JArray a&&a.Count==3,"lightmap-triple");foreach(var index in (JArray)lm)JsonData.Integer(index,-1,textures.Count-1);JsonData.Require((int)lm[0]>=0&&(mode=="directional"?(int)lm[1]>=0:(int)lm[1]==-1),"lightmap-mode-textures");}
   var nodes=JsonData.Array(value["nodes"],16384);
   for(int i=0;i<nodes.Count;i++){
    var n=nodes[i] as JObject;var fields=new[]{"name","parent","position","rotation","scale","mesh","materials","lightmap","lightmapST"};if(n!=null&&n.ContainsKey("layer")){JsonData.Fields(n,fields.Concat(new[]{"layer"}).ToArray());JsonData.Integer(n["layer"],0,31);}else JsonData.Fields(n,fields);
    JsonData.Text(n["name"],128);JsonData.Integer(n["parent"],-1,i-1);
    JsonData.Vector(n["position"],3);JsonData.Vector(n["rotation"],4,-1,1);JsonData.Vector(n["scale"],3,-10000,10000);
    double q=((JArray)n["rotation"]).Sum(v=>(double)v*(double)v);JsonData.Require(Math.Abs(q-1)<.001,"rotation-normalization");
    JsonData.Integer(n["mesh"],-1,meshes.Count-1);foreach(var m in JsonData.Array(n["materials"],64))JsonData.Integer(m,0,materials.Count-1);
    JsonData.Integer(n["lightmap"],-1,lightmaps.Count-1);JsonData.Vector(n["lightmapST"],4);
   }
   var env=value["environment"] as JObject;JsonData.Fields(env,"ambient","fog","fogColor","fogDensity");JsonData.Vector(env["ambient"],4,0,16);JsonData.Vector(env["fogColor"],4,0,16);
   JsonData.Require(env["fog"]?.Type==JTokenType.Boolean,"fog-bool");JsonData.Number(env["fogDensity"],0,1);
   if(value.ContainsKey("effects"))ControlledData.Validate(value);
   return value;
  }
  static JArray ResourceList(JToken token){
   var items=JsonData.Array(token,4096);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in items)JsonData.Require(names.Add(JsonData.Text(item)),"duplicate-resource-reference");
   return items;
  }
 }
}

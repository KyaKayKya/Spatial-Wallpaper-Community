using System;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Security {
 public static class ValueContract {
  // Reference resolution is supplied by admission or the owned runtime domain.
  public static void Check(JToken value,JObject constraint,Action<string,JToken> reference){
   string type=(string)constraint["type"];
   JsonData.Require(value!=null,"missing-value");
   if(value.Type==JTokenType.Null){JsonData.Require((bool)constraint["nullable"],"null-not-allowed");return;}
   if(type.EndsWith("[]",StringComparison.Ordinal)){foreach(var item in JsonData.Array(value,(int)constraint["max_elements"]))Check(item,RuleCatalog.Current.Constraint(type.Substring(0,type.Length-2)),reference);return;}
   switch(type){
    case "bool":JsonData.Require(value.Type==JTokenType.Boolean,"expected-bool");return;
    case "i32":JsonData.Integer(value,(int)constraint["min"],(int)constraint["max"]);return;
    case "f32":JsonData.Number(value,(double)constraint["min"],(double)constraint["max"]);return;
    case "string":JsonData.Text(value,(int)constraint["max_chars"]);return;
    case "vector3":case "color":JsonData.Vector(value,(int)constraint["length"],(double)constraint["min"],(double)constraint["max"]);return;
    case "quaternion":JsonData.Vector(value,4,-1,1);double squared=0;foreach(var part in (JArray)value)squared+=(double)part*(double)part;JsonData.Require(Math.Abs(squared-1)<.001,"rotation-normalization");return;
    default:reference(type,value);return;
   }
  }
 }
}

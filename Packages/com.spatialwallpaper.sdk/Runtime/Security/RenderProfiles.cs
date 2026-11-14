using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
namespace SpatialWallpaper.Sdk.Security {
 // Compiled into exporter, Validator and trusted Worker. Package data can select
 // an exact identity; it cannot supply new settings or RendererFeature code.
 public static class RenderProfiles {
  static readonly JObject catalog=JObject.Parse(RenderProfileSource.Json);
  static readonly Dictionary<string,JObject> profiles=Load();
  static Dictionary<string,JObject> Load(){var result=new Dictionary<string,JObject>(StringComparer.Ordinal);foreach(JObject profile in (JArray)catalog["profiles"])result.Add((string)profile["id"],profile);return result;}
  public static JObject Identity(JObject profile)=>new JObject{{"id",profile["id"].DeepClone()},{"version",profile["version"].DeepClone()},{"format_version",profile["format_version"].DeepClone()},{"descriptor_sha256",PackageValidator.Identity(profile)}};
  public static JObject DefaultIdentity=>Identity(profiles[(string)catalog["default"]]);
  public static void RequireObservation(JToken identity,JObject observation){
   var expected=Require(identity);
   JsonData.Require(observation!=null,"render-profile-observation-required");
   JsonData.Fields(observation,"engine","color_space","graphics_api","settings","renderer_features","pipeline_type","renderer_type","post_process_data","camera","capture");
   foreach(string key in new[]{"engine","color_space","graphics_api","settings","renderer_features","camera","capture"})
    JsonData.Require(JToken.DeepEquals(expected[key],observation[key]),"render-profile-observation-"+key);
   JsonData.Require((string)observation["pipeline_type"]=="UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset","render-profile-observation-pipeline_type");
   JsonData.Require((string)observation["renderer_type"]=="UnityEngine.Rendering.Universal.UniversalRenderer","render-profile-observation-renderer_type");
   JsonData.Require(observation["post_process_data"]?.Type==JTokenType.Boolean&&(bool)observation["post_process_data"],"render-profile-observation-post_process_data");
  }
  public static JObject Require(JToken value){
   JsonData.Require(value is JObject,"render-profile-required");var request=(JObject)value;
   JsonData.Fields(request,"id","version","format_version","descriptor_sha256");
   string id=JsonData.Text(request["id"]);JsonData.Require(profiles.TryGetValue(id,out var profile),"render-profile-unknown");
   JsonData.Require(JsonData.Text(request["version"])==(string)profile["version"],"render-profile-version");
   JsonData.Require(request["format_version"]?.Type==JTokenType.Integer&&(long)request["format_version"]==(int)profile["format_version"],"render-profile-format");
   JsonData.Require(JsonData.Text(request["descriptor_sha256"])==PackageValidator.Identity(profile),"render-profile-digest");
   return (JObject)profile.DeepClone();
  }
 }
}

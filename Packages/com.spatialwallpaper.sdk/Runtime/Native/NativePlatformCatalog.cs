using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
namespace SpatialWallpaper.Sdk.Native {
 // The caller supplies the index embedded in the trusted product, never a package index.
 public sealed class NativePlatformCatalog {
  readonly JObject index;
  public NativePlatformCatalog(string json){index=JsonData.Parse(json);JsonData.Fields(index,"format","version","scenes","build_id");JsonData.Require((string)index["format"]=="spatial-native-platform-index"&&(int?)index["version"]==1&&(string)index["build_id"]==PackageValidator.Identity(index),"native-platform-index-identity");var scenes=index["scenes"] as JArray;JsonData.Require(scenes!=null&&scenes.Count>0&&scenes.Count<=64&&scenes.Select(s=>(string)s["id"]).Distinct(StringComparer.Ordinal).Count()==scenes.Count,"native-platform-scenes");}
  public JObject Scene(string id){var scene=((JArray)index["scenes"]).OfType<JObject>().SingleOrDefault(value=>(string)value["id"]==id);JsonData.Require(scene!=null,"native-platform-scene-unavailable");return (JObject)scene.DeepClone();}
  public void Require(NativePackage package){var scene=Scene(package.SceneId);package.RequireTrustedScene((string)scene["library_digest"],(string)scene["source_sha256"]);var assets=(JArray)scene["assets"];foreach(JObject resource in (JArray)package.Manifest["resources"])JsonData.Require(assets.Any(asset=>(string)asset["key"]==(string)resource["asset_key"]&&(string)asset["kind"]==(string)resource["kind"]&&(string)asset["source_sha256"]==(string)resource["source_sha256"]),"native-platform-resource-identity");}
 }
}

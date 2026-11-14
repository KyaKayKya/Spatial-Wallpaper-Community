using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialWallpaper.Content;

namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativeSceneLibrary:ScriptableObject {
  [Serializable]public sealed class AssetRecord {public string Key,Kind,SourceDigest;public UnityEngine.Object Asset;}
  [Serializable]public sealed class SceneRecord {public string Id,SourceDigest;public AssetRecord[] Assets;}
  [SerializeField]SceneRecord[] scenes;
  [SerializeField]string digest;
  public string Digest=>digest;
  public static NativeSceneLibrary Create(SceneRecord[] records){
   if(records==null||records.Length==0||records.Length>64||records.Any(record=>record==null)||records.Select(record=>record.Id).Distinct(StringComparer.Ordinal).Count()!=records.Length)throw new ArgumentException("native-scene-library-scenes");var result=CreateInstance<NativeSceneLibrary>();result.scenes=records;result.digest=PackageValidator.Identity(result.Metadata());result.Require(result.digest);return result;
  }
  JObject Metadata()=>new JObject{{"format","spatial-native-platform-library"},{"version",1},{"sdk",SceneCommandCatalog.TargetSdk},{"catalog_digest",SceneCommandCatalog.Digest},{"scenes",new JArray(scenes.OrderBy(scene=>scene.Id,StringComparer.Ordinal).Select(scene=>new JObject{{"id",scene.Id},{"source_sha256",scene.SourceDigest},{"assets",new JArray(scene.Assets.OrderBy(asset=>asset.Key,StringComparer.Ordinal).Select(asset=>new JObject{{"key",asset.Key},{"kind",asset.Kind},{"source_sha256",asset.SourceDigest}}))}}))}};
  public JObject Manifest{get{Require(digest);return Metadata();}}
  public void Require(string expectedDigest){
   if(string.IsNullOrEmpty(expectedDigest)||digest!=expectedDigest||scenes==null||scenes.Length==0||digest!=PackageValidator.Identity(Metadata()))throw new InvalidOperationException("native-scene-library-identity");
   foreach(var scene in scenes){if(string.IsNullOrWhiteSpace(scene.Id)||scene.SourceDigest==null||scene.SourceDigest.Length!=64||!scene.SourceDigest.All(Uri.IsHexDigit)||scene.Assets.Length>SceneCommandCatalog.MaxObjects||scene.Assets.Select(asset=>asset.Key).Distinct(StringComparer.Ordinal).Count()!=scene.Assets.Length)throw new InvalidOperationException("native-scene-library-catalog");foreach(var asset in scene.Assets)new NativeAssetBinding(asset.Key,asset.Kind,asset.Asset,asset.SourceDigest);}
  }
  public NativeAssetBinding Resolve(string sceneId,string key,string kind,string sourceDigest,string alias){
   var scene=scenes.SingleOrDefault(value=>value.Id==sceneId)??throw new InvalidOperationException("native-scene-library-scene");var asset=scene.Assets.SingleOrDefault(value=>value.Key==key)??throw new InvalidOperationException("native-scene-library-resource");if(asset.Kind!=kind||asset.SourceDigest!=sourceDigest)throw new InvalidOperationException("native-scene-library-resource-identity");return new NativeAssetBinding(alias,kind,asset.Asset,sourceDigest);
  }
  public static string ResourcePath(string sceneId){if(string.IsNullOrEmpty(sceneId)||sceneId.Length>64||!sceneId.All(character=>character>='a'&&character<='z'||character>='0'&&character<='9'||character=='-'||character=='_'))throw new ArgumentException("native-scene-library-id");return "SpatialWallpaper/NativeSceneLibraries/"+sceneId;}
 }
}

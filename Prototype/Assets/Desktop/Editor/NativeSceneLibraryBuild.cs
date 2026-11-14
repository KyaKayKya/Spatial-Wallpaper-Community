#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using SpatialWallpaper.Library;
using SpatialWallpaper.Sdk.Native.Unity;
using SpatialWallpaper.Content;

namespace SpatialWallpaper.Desktop.Editor {
 public static class NativeSceneLibraryBuild {
  const string LibraryDirectory="Assets/Desktop/Resources/SpatialWallpaper/NativeSceneLibraries";
  static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static string Kind(UnityEngine.Object value)=>value is Material?"material":value is Mesh?"mesh":value is Cubemap?"cubemap":value is Texture2D?"texture":value is AudioClip?"sound":value is GameObject&&PrefabUtility.IsPartOfPrefabAsset(value)?"prefab":null;
  public static void Generate(){
   string output=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_LIBRARY_OUTPUT");if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("explicit-native-library-output-required");Directory.CreateDirectory(output);var sourceRecords=new JObject();var hashes=new Dictionary<string,string>(StringComparer.Ordinal);var scenes=new List<NativeSceneLibrary.SceneRecord>();var audioRecords=new JArray();
   string Provenance(string path){if(hashes.TryGetValue(path,out var existing))return existing;string physical=Path.GetFullPath(path);string file=File.Exists(physical)?Hash(File.ReadAllBytes(physical)):null;string meta=File.Exists(physical+".meta")?Hash(File.ReadAllBytes(physical+".meta")):null;string imported=AssetDatabase.GetAssetDependencyHash(path).ToString();string digest=Hash(Encoding.UTF8.GetBytes(path+"\n"+file+"\n"+meta+"\n"+imported+"\n"+Application.unityVersion));hashes.Add(path,digest);sourceRecords[path]=new JObject{{"file_sha256",file},{"meta_sha256",meta},{"import_dependency_hash",imported},{"provenance_sha256",digest}};return digest;}
   foreach(var artwork in ArtworkCatalog.Builtins){
    if(!File.Exists(artwork.ScenePath))throw new FileNotFoundException("native-library-scene",artwork.ScenePath);var paths=new HashSet<string>(AssetDatabase.GetDependencies(artwork.ScenePath,true),StringComparer.Ordinal);
    bool music=new[]{"showcase","parking","train","rainycafe","minishotgame"}.Contains(artwork.Id);string audio="Assets/SdkNativeAudio/"+artwork.Id;
    if(music){if(!AssetDatabase.IsValidFolder(audio))throw new DirectoryNotFoundException("native-original-audio-not-staged:"+audio);var clips=AssetDatabase.FindAssets("t:AudioClip",new[]{audio});if(clips.Length==0)throw new InvalidOperationException("native-original-audio-empty:"+audio);foreach(string guid in clips){string path=AssetDatabase.GUIDToAssetPath(guid);var importer=(AudioImporter)AssetImporter.GetAtPath(path);var settings=importer.defaultSampleSettings;settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.compressionFormat=AudioCompressionFormat.PCM;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.preloadAudioData=false;importer.defaultSampleSettings=settings;importer.forceToMono=false;importer.loadInBackground=true;importer.SaveAndReimport();paths.Add(path);}}
    var assets=new Dictionary<string,NativeSceneLibrary.AssetRecord>(StringComparer.Ordinal);
    foreach(string path in paths.OrderBy(value=>value,StringComparer.Ordinal)){
     string source=Provenance(path);foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path)){
      if(!asset)continue;string kind=Kind(asset);if(kind==null)continue;if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long local))throw new InvalidOperationException("native-library-resource-id:"+path);string key="r_"+guid+"_"+unchecked((ulong)local).ToString("x16");string digest=Hash(Encoding.UTF8.GetBytes(source+"\n"+key+"\n"+kind));if(!assets.ContainsKey(key))assets.Add(key,new NativeSceneLibrary.AssetRecord{Key=key,Kind=kind,SourceDigest=digest,Asset=asset});
      var inventory=sourceRecords[path]["assets"] as JArray;if(inventory==null){inventory=new JArray();sourceRecords[path]["assets"]=inventory;}if(!inventory.Any(record=>(string)record["key"]==key))inventory.Add(new JObject{{"key",key},{"name",asset.name},{"kind",kind},{"source_sha256",digest}});
      if(asset is AudioClip clip&&path.StartsWith("Assets/SdkNativeAudio/",StringComparison.Ordinal)){bool effect=Path.GetFileName(path)=="Hit.wav"||Path.GetFileName(path)=="Reset.wav";if(!effect&&(clip.frequency!=44100||clip.channels!=2))throw new InvalidOperationException("native-original-music-quality-mismatch:"+path);audioRecords.Add(new JObject{{"scene",artwork.Id},{"key",key},{"file",path},{"frequency",clip.frequency},{"channels",clip.channels},{"samples",clip.samples},{"load_state",clip.loadState.ToString()},{"quality","original source and preserved sample rate/channel count; native PCM import, no legacy normalized sound encoding"}});}
     }
    }
    string sceneDigest=Hash(Encoding.UTF8.GetBytes(string.Join("\n",paths.OrderBy(value=>value,StringComparer.Ordinal).Select(path=>path+":"+Provenance(path)))));scenes.Add(new NativeSceneLibrary.SceneRecord{Id=artwork.Id,SourceDigest=sceneDigest,Assets=assets.Values.OrderBy(value=>value.Key,StringComparer.Ordinal).ToArray()});
   }
   Directory.CreateDirectory(LibraryDirectory);AssetDatabase.Refresh();var catalog=new JArray();
   foreach(var scene in scenes){var library=NativeSceneLibrary.Create(new[]{scene});string path=LibraryDirectory+"/"+scene.Id+".asset";var saved=AssetDatabase.LoadAssetAtPath<NativeSceneLibrary>(path);if(saved){EditorUtility.CopySerialized(library,saved);UnityEngine.Object.DestroyImmediate(library);library=saved;EditorUtility.SetDirty(saved);}else AssetDatabase.CreateAsset(library,path);catalog.Add(new JObject{{"id",scene.Id},{"source_sha256",scene.SourceDigest},{"library_digest",library.Digest},{"resource_path",NativeSceneLibrary.ResourcePath(scene.Id)},{"assets",library.Manifest["scenes"][0]["assets"].DeepClone()}});}
   const string previous="Assets/Desktop/Resources/SpatialWallpaper/NativeSceneLibrary.asset";var combined=AssetDatabase.LoadAssetAtPath<NativeSceneLibrary>(previous);if(combined){if(combined.Digest!="f7056bd247d2fe2f5d7c604efda854df4dfc5e0e7a1d7ae25982c8b784ec575a")throw new InvalidOperationException("native-library-refuse-removing-unknown-combined-catalog");if(!AssetDatabase.DeleteAsset(previous))throw new IOException("native-library-combined-catalog-retirement-failed");}AssetDatabase.SaveAssets();
   var index=new JObject{{"format","spatial-native-platform-index"},{"version",1},{"scenes",catalog}};index["build_id"]=PackageValidator.Identity(index);
   File.WriteAllText("Assets/Desktop/Resources/SpatialWallpaper/NativePlatformIndex.json",index.ToString());AssetDatabase.ImportAsset("Assets/Desktop/Resources/SpatialWallpaper/NativePlatformIndex.json");File.WriteAllText(Path.Combine(output,"native-platform-library.json"),index.ToString());File.WriteAllText(Path.Combine(output,"source-provenance.json"),sourceRecords.ToString());File.WriteAllText(Path.Combine(output,"audio-imports.json"),audioRecords.ToString());File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"asset_directory",LibraryDirectory},{"library_digest",index["build_id"]},{"scenes",scenes.Count},{"resources",scenes.Sum(scene=>scene.Assets.Length)},{"load_scope","one original scene asset library per renderer; never load the eight-scene resource union"},{"quality_policy","original Unity object references; no mesh/texture downsampling or material replacement; original audio with preserved sample rate/channels"},{"scope","trusted original platform library generation, not foreign asset admission, Player fidelity or final native packs"}}.ToString());
  }
 }
}
#endif

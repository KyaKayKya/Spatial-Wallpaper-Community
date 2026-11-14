using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only resource evidence for the original scenes. Never regenerates or saves scenes.
public static class NativeBaselineAudit {
 public static void CreateBlueprintFixture(){
  string input=Environment.GetEnvironmentVariable("SPATIAL_BASELINE_BLUEPRINT_INPUT"),output=Environment.GetEnvironmentVariable("SPATIAL_BASELINE_BLUEPRINT_STORE");
  if(string.IsNullOrEmpty(input)||string.IsNullOrEmpty(output)||Directory.Exists(output))throw new ArgumentException("Use an input and a fresh owned blueprint store");
  byte[] bytes=File.ReadAllBytes(input);
  var parsed=SpatialWallpaper.Blueprint.BlueprintReader.Read(bytes,Path.GetFileName(input),System.Threading.CancellationToken.None);
  var store=new SpatialWallpaper.Blueprint.BlueprintWorkspace(output);
  var work=store.Create("Native baseline controlled cabin",parsed.SourceHash,bytes,new SpatialWallpaper.Blueprint.BlueprintSettings{Look="natural",Hour=12,Yaw=20});
  File.WriteAllText(Path.Combine(output,"fixture.json"),new JObject{["input_sha256"]=Digest(input),["source_hash"]=parsed.SourceHash,["artwork"]="blueprint-"+work.WorkId+"-"+work.RevisionId,["blocks"]=parsed.BlockCount,["scope"]="controlled test structure; imported with original BlueprintReader and BlueprintWorkspace"}.ToString());
 }
 static string Identity(UnityEngine.Object value){
  if(!value)return "null";
  if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string guid,out long id))return guid+":"+id;
  return "builtin:"+value.GetType().Name+":"+value.name;
 }
 static string Digest(string path){using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static JObject Observe(string path){
  var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  var roots=scene.GetRootGameObjects();
  var renderers=roots.SelectMany(root=>root.GetComponentsInChildren<Renderer>(true)).ToArray();
  var records=new JArray();
  foreach(var renderer in renderers){
   var filter=renderer.GetComponent<MeshFilter>();var skin=renderer as SkinnedMeshRenderer;
   var mesh=skin?skin.sharedMesh:filter?filter.sharedMesh:null;
   records.Add(new JObject{
    ["name"]=renderer.name,["type"]=renderer.GetType().Name,["active"]=renderer.gameObject.activeInHierarchy,["enabled"]=renderer.enabled,
    ["mesh"]=Identity(mesh),["vertices"]=mesh?mesh.vertexCount:0,["submeshes"]=mesh?mesh.subMeshCount:0,
    ["indices"]=mesh?new JArray(Enumerable.Range(0,mesh.subMeshCount).Select(index=>(long)mesh.GetIndexCount(index))):new JArray(),
    ["attributes"]=mesh?new JArray(mesh.GetVertexAttributes().Select(attribute=>attribute.ToString())):new JArray(),
    ["materials"]=new JArray(renderer.sharedMaterials.Select(Identity)),
    ["shaders"]=new JArray(renderer.sharedMaterials.Select(material=>material?Identity(material.shader):"null")),
    ["lightmap_index"]=renderer.lightmapIndex,["lightmap_scale_offset"]=new JArray(renderer.lightmapScaleOffset.x,renderer.lightmapScaleOffset.y,renderer.lightmapScaleOffset.z,renderer.lightmapScaleOffset.w),
    ["shadow_casting"]=renderer.shadowCastingMode.ToString(),["receive_shadows"]=renderer.receiveShadows,
    ["bones"]=skin?skin.bones.Length:0
   });
  }
  var assets=new JObject();
  foreach(string file in AssetDatabase.GetDependencies(path,true).OrderBy(file=>file,StringComparer.Ordinal)){
   if(File.Exists(file))assets[file]=new JObject{["sha256"]=Digest(file),["bytes"]=new FileInfo(file).Length};
  }
  return new JObject{
   ["scene"]=path,["scene_sha256"]=Digest(path),["renderers"]=records,["dependencies"]=assets,
   ["animators"]=new JArray(roots.SelectMany(root=>root.GetComponentsInChildren<Animator>(true)).Select(animator=>new JObject{["name"]=animator.name,["active"]=animator.gameObject.activeInHierarchy,["enabled"]=animator.enabled,["controller"]=Identity(animator.runtimeAnimatorController),["avatar"]=Identity(animator.avatar)})),
   ["audio"]=new JArray(roots.SelectMany(root=>root.GetComponentsInChildren<AudioSource>(true)).Select(audio=>new JObject{["name"]=audio.name,["active"]=audio.gameObject.activeInHierarchy,["enabled"]=audio.enabled,["clip"]=Identity(audio.clip),["loop"]=audio.loop,["volume"]=audio.volume})),
   ["lights"]=new JArray(roots.SelectMany(root=>root.GetComponentsInChildren<Light>(true)).Select(light=>new JObject{["name"]=light.name,["active"]=light.gameObject.activeInHierarchy,["enabled"]=light.enabled,["type"]=light.type.ToString(),["intensity"]=light.intensity,["range"]=light.range,["color"]=new JArray(light.color.r,light.color.g,light.color.b,light.color.a),["shadows"]=light.shadows.ToString()})),
   ["lightmaps"]=new JArray(LightmapSettings.lightmaps.Select(map=>new JObject{["color"]=Identity(map.lightmapColor),["direction"]=Identity(map.lightmapDir),["shadowmask"]=Identity(map.shadowMask)}))
  };
 }
 static string ResourceIdentity(JToken resource){var identity=(JObject)resource.DeepClone();identity.Remove("name");return identity.ToString(Newtonsoft.Json.Formatting.None);}
 static JArray Missing(JArray original,JArray native){
  var remaining=native.Select(ResourceIdentity).ToList();var missing=new JArray();
  foreach(var item in original)if(!remaining.Remove(ResourceIdentity(item)))missing.Add(item.DeepClone());
  return missing;
 }
 static readonly string[] ResourceKinds={"renderers","animators","audio","lights","lightmaps"};
 static JObject Compare(JObject original,JObject native,string id){
  var comparison=new JObject{["artwork"]=id,["passed"]=true};
  foreach(string kind in ResourceKinds){
   var missing=Missing((JArray)original[kind],(JArray)native[kind]);
   comparison["original_"+kind]=((JArray)original[kind]).Count;comparison["native_"+kind]=((JArray)native[kind]).Count;
   comparison["missing_original_"+kind]=missing;if(missing.Count!=0)comparison["passed"]=false;
  }
  var changed=new JArray();
  foreach(var file in ((JObject)original["dependencies"]).Properties())if(native["dependencies"][file.Name]!=null&&!JToken.DeepEquals(file.Value,native["dependencies"][file.Name]))changed.Add(file.Name);
  comparison["changed_shared_dependencies"]=changed;if(changed.Count!=0)comparison["passed"]=false;
  return comparison;
 }
 static JObject CheckComparison(){
  var source=new JObject{["dependencies"]=new JObject{["material"]="sha-original"}};
  foreach(string kind in ResourceKinds)source[kind]=new JArray(new JObject{["name"]="original",["enabled"]=true,["asset"]="original-"+kind});
  if(!(bool)Compare(source,(JObject)source.DeepClone(),"positive")["passed"])throw new Exception("baseline comparer rejected identical resources");
  foreach(string kind in ResourceKinds){var changed=(JObject)source.DeepClone();changed[kind]=new JArray();if((bool)Compare(source,changed,kind)["passed"])throw new Exception("baseline comparer accepted missing "+kind);}
  var disabled=(JObject)source.DeepClone();disabled["renderers"][0]["enabled"]=false;if((bool)Compare(source,disabled,"disabled")["passed"])throw new Exception("baseline comparer accepted disabled renderer");
  var substituted=(JObject)source.DeepClone();substituted["renderers"][0]["asset"]="substitute";if((bool)Compare(source,substituted,"substitute")["passed"])throw new Exception("baseline comparer accepted changed render asset");
  var dependency=(JObject)source.DeepClone();dependency["dependencies"]["material"]="sha-changed";if((bool)Compare(source,dependency,"dependency")["passed"])throw new Exception("baseline comparer accepted changed dependency");
  return new JObject{["passed"]=true,["identical_positive"]=true,["missing_resource_groups_rejected"]=new JArray(ResourceKinds),["disabled_renderer_rejected"]=true,["substituted_asset_rejected"]=true,["changed_shared_dependency_rejected"]=true};
 }
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_BASELINE_AUDIT")??"../Validation/Content/sdk-runtime/native-render/task1/assets";
  Directory.CreateDirectory(output);
  File.WriteAllText(Path.Combine(output,"comparer-checks.json"),CheckComparison().ToString());
  string[] sources={"Assets/Showcase/Scenes/Showcase.unity","Assets/Pool/Scenes/Pool.unity","Assets/Train/Scenes/Train.unity","Assets/Parking/Scenes/Parking.unity","Assets/MiniShotGame/Scenes/MiniShotGame.unity","Assets/Limpool/Scenes/Limpool.unity","Assets/RainyCafe/Scenes/RainyCafe.unity","Assets/Mita/Generated/Mita.unity"};
  var comparisons=new JArray();
  for(int index=0;index<sources.Length;index++){
   string id=SpatialWallpaper.Library.ArtworkCatalog.Builtins[index].Id;
   string only=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_BASELINE_ONLY");if(!string.IsNullOrEmpty(only)&&only!=id)continue;
   var original=Observe(sources[index]);
   string prepared=id=="mita"?Environment.GetEnvironmentVariable("SPATIAL_CANONICAL_MITA_SCENE"):null;
   var native=Observe(prepared??SpatialWallpaper.Library.ArtworkCatalog.Builtins[index].ScenePath);
   File.WriteAllText(Path.Combine(output,id+"-original.json"),original.ToString());
   File.WriteAllText(Path.Combine(output,id+"-native.json"),native.ToString());
   comparisons.Add(Compare(original,native,id));
  }
  File.WriteAllText(Path.Combine(output,"comparison.json"),comparisons.ToString());
  if(comparisons.Any(item=>(bool)item["passed"]!=true))throw new InvalidDataException("Original scene resources differ; inspect comparison.json");
 }
}

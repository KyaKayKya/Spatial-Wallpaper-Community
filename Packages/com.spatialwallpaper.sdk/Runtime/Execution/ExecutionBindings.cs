using System;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Execution {
 public static class RenderingBindings {
  static readonly HashSet<string> Members=new HashSet<string>(new[]{
   "SpatialWallpaper.Sdk::Sound::PlayMusic(UnityEngine.AudioClip,System.Single)->System.Void",
   "SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void",
   "SpatialWallpaper.Sdk::Sound::StopMusic()->System.Void",
   "SpatialWallpaper.Sdk::Sound::StopEffects()->System.Void",
   "SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3",
   "SpatialWallpaper.Sdk::View::GetSize()->UnityEngine.Vector3",
   "SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node",
   "SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Transform::Rotate(System.Single,System.Single,System.Single)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3",
   "UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Light::get_intensity()->System.Single",
   "UnityEngine.CoreModule::UnityEngine.Light::set_intensity(System.Single)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Mesh::Clear()->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",
   "SpatialWallpaper.Sdk::Resources::Release(Mesh)->System.Void",
   "SpatialWallpaper.Sdk::Resources::Release(Texture)->System.Void",
   "SpatialWallpaper.Sdk::Resources::Release(Material)->System.Void",
   "SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh",
   "SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture",
   "SpatialWallpaper.Sdk::Resources::CreateMaterial(Material)->Material",
   "UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateNormals()->System.Void",
   "UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateBounds()->System.Void",
   "SpatialWallpaper.Sdk::Resources::BindMesh(Node,Mesh)->System.Void"
  },StringComparer.Ordinal);
  public static bool Supports(string member)=>Members.Contains(member);
 }
 public static class ExecutionBindings {
  // Pure preflight: run before constructing any native content or invoking author callbacks.
  public static void Require(AdmittedBuild build,Func<string,bool> hostSupports){
   if(build==null||hostSupports==null)throw new ArgumentNullException();
   foreach(JObject cls in (JArray)build.Program["classes"])foreach(JObject method in (JArray)cls["methods"])foreach(JObject instruction in (JArray)method["body"]){
    if((string)instruction["op"]!="call")continue;string member=(string)instruction["value"];
    if(!CoreLibrary.Supports(member)&&!hostSupports(member))throw new InvalidOperationException("missing-host-binding:"+member);
   }
  }
 }
}

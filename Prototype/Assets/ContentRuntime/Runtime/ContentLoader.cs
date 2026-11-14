using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialWallpaper.Content {
 public sealed class SceneLoadRequest {
  public ValidatedPackage Package;public Camera Camera;public double ViewportWidth,ViewportHeight;public bool KeepHeight=true;
 }
 public sealed class ContentHandle {
  public GameObject Root{get;internal set;}public string BuildId{get;internal set;}
  public float ContentDepth{get;internal set;}
  internal readonly List<UnityEngine.Object> Assets=new List<UnityEngine.Object>();
  internal readonly List<AssetBundle> Bundles=new List<AssetBundle>();
  internal Camera Camera;internal Color CameraBackground;
  internal WindowProjection Projection;internal float PreviousProjectionDepth;
  internal Action RestoreCameraSettings;
  internal LightmapData[] PreviousLightmaps;internal LightmapsMode PreviousLightmapMode;internal Color Ambient,AmbientSky,AmbientEquator,AmbientGround,FogColor;internal AmbientMode AmbientMode;internal SphericalHarmonicsL2 AmbientProbe;internal bool Fog;internal float FogDensity;internal bool Unloaded;
 }
 // All calls run on Unity's main thread. There is one active content owner per
 // process; Manager preview and Player instantiate the same code separately.
 public sealed class ContentLoader {
  ContentHandle active;
  public async Task<ContentHandle> LoadAsync(SceneLoadRequest request,CancellationToken cancellation){
   // Current schema2 native shader payloads are validated only for Win64/D3D11.
   // Refuse before touching presentation state; never feed them to Metal.
   if(!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))throw new PlatformNotSupportedException("SDK schema2 当前只支持 Windows/D3D11；macOS Shader Bundle 准入尚未实现，请使用内置作品");
   if(active!=null)throw new InvalidOperationException("content-already-owned");
   if(request?.Package==null||!request.Camera)throw new ArgumentException("missing-load-context");
   var scene=request.Package.Scene;var frame=(JArray)scene["frame"];
   float scale=(float)Framing.Scale((double)frame[0],(double)frame[1],request.ViewportWidth,request.ViewportHeight,request.KeepHeight);
   var handle=new ContentHandle{BuildId=request.Package.BuildId,PreviousLightmaps=LightmapSettings.lightmaps,PreviousLightmapMode=LightmapSettings.lightmapsMode,Ambient=RenderSettings.ambientLight,AmbientSky=RenderSettings.ambientSkyColor,AmbientEquator=RenderSettings.ambientEquatorColor,AmbientGround=RenderSettings.ambientGroundColor,AmbientMode=RenderSettings.ambientMode,Fog=RenderSettings.fog,FogColor=RenderSettings.fogColor,FogDensity=RenderSettings.fogDensity};
   handle.Camera=request.Camera;handle.CameraBackground=request.Camera.backgroundColor;handle.ContentDepth=(float)frame[2];handle.Projection=request.Camera.GetComponent<WindowProjection>();if(handle.Projection)handle.PreviousProjectionDepth=handle.Projection.contentDepth;handle.AmbientProbe=RenderSettings.ambientProbe;active=handle;handle.Root=new GameObject("Content-"+handle.BuildId.Substring(0,12));handle.Root.SetActive(false);handle.Root.transform.localScale=Vector3.one*scale;
   try {
    cancellation.ThrowIfCancellationRequested();
    var meshes=((JArray)scene["meshes"]).Select(p=>ReadMesh(request.Package.Read((string)p),handle)).ToArray();
    var textures=((JArray)scene["textures"]).Select(p=>ReadTexture(request.Package.Read((string)p),handle)).ToArray();
    var bundles=new Dictionary<string,AssetBundle>(StringComparer.Ordinal);var materials=new List<Material>();
    foreach(JObject m in (JArray)scene["materials"]){
     cancellation.ThrowIfCancellationRequested();Shader shader;string bundleName=(string)m["bundle"];
     if(bundleName=="")shader=Shader.Find((string)m["shader"]);
     else {
      if(!bundles.TryGetValue(bundleName,out var bundle)){
       bundle=AssetBundle.LoadFromMemory(request.Package.Read(bundleName));if(!bundle)throw new InvalidDataException("shader-bundle-load-failed");bundles.Add(bundleName,bundle);handle.Bundles.Add(bundle);
      }
      shader=bundle.LoadAllAssets<Shader>().SingleOrDefault(s=>s.name==(string)m["shader"]);
     }
     if(!shader||!shader.isSupported)throw new InvalidDataException("unsupported-shader:"+(string)m["shader"]);
     var material=new Material(shader){enableInstancing=(bool)m["instancing"],renderQueue=(int)m["queue"]};handle.Assets.Add(material);
     material.shaderKeywords=((JArray)m["keywords"]).Select(k=>(string)k).ToArray();
     foreach(var p in ((JObject)m["properties"]).Properties()){
      if(!material.HasProperty(p.Name))throw new InvalidDataException("missing-shader-property:"+p.Name);var v=(JObject)p.Value;
      switch((string)v["type"]){case "float":material.SetFloat(p.Name,(float)v["value"]);break;
       case "color":material.SetColor(p.Name,ColorValue(v["value"]));break;case "vector":material.SetVector(p.Name,Vector4Value(v["value"]));break;
       case "texture":int index=(int)v["value"];material.SetTexture(p.Name,index<0?null:textures[index]);break;
      }
     }
     materials.Add(material);
    }
    var nodes=new List<GameObject>();
    foreach(JObject n in (JArray)scene["nodes"]){
     cancellation.ThrowIfCancellationRequested();var go=new GameObject((string)n["name"]);go.layer=n["layer"]!=null?(int)n["layer"]:0;nodes.Add(go);int parent=(int)n["parent"];
     go.transform.SetParent(parent<0?handle.Root.transform:nodes[parent].transform,false);go.transform.localPosition=Vector3Value(n["position"]);go.transform.localRotation=QuaternionValue(n["rotation"]);go.transform.localScale=Vector3Value(n["scale"]);
     int mesh=(int)n["mesh"];if(mesh>=0){go.AddComponent<MeshFilter>().sharedMesh=meshes[mesh];var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=((JArray)n["materials"]).Select(v=>materials[(int)v]).ToArray();renderer.lightmapIndex=(int)n["lightmap"];renderer.lightmapScaleOffset=Vector4Value(n["lightmapST"]);}
    }
    LightmapSettings.lightmapsMode=(string)scene["lightmapMode"]=="directional"?LightmapsMode.CombinedDirectional:LightmapsMode.NonDirectional;
    LightmapSettings.lightmaps=((JArray)scene["lightmaps"]).Select(p=>{var a=(JArray)p;return new LightmapData{lightmapColor=TextureAt(textures,(int)a[0]),lightmapDir=TextureAt(textures,(int)a[1]),shadowMask=TextureAt(textures,(int)a[2])};}).ToArray();
    var environment=(JObject)scene["environment"];RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=ColorValue(environment["ambient"]);RenderSettings.fog=(bool)environment["fog"];RenderSettings.fogColor=ColorValue(environment["fogColor"]);RenderSettings.fogDensity=(float)environment["fogDensity"];
    ControlledEffects.Build(scene["effects"] as JArray,nodes,meshes,materials,handle,request.Camera,scale);
    if(handle.Projection)handle.Projection.contentDepth=handle.ContentDepth*scale;
    handle.Root.SetActive(true);await Task.Yield();cancellation.ThrowIfCancellationRequested();return handle;
   }catch {await UnloadAsync(handle);throw;}
  }
  public async Task UnloadAsync(ContentHandle handle){
   if(handle==null||handle.Unloaded)return;if(active!=handle)throw new InvalidOperationException("foreign-content-handle");handle.Unloaded=true;
   if(handle.Root){handle.Root.SetActive(false);UnityEngine.Object.Destroy(handle.Root);}
   LightmapSettings.lightmaps=handle.PreviousLightmaps;LightmapSettings.lightmapsMode=handle.PreviousLightmapMode;RenderSettings.ambientMode=handle.AmbientMode;RenderSettings.ambientLight=handle.Ambient;RenderSettings.ambientSkyColor=handle.AmbientSky;RenderSettings.ambientEquatorColor=handle.AmbientEquator;RenderSettings.ambientGroundColor=handle.AmbientGround;RenderSettings.fog=handle.Fog;RenderSettings.fogColor=handle.FogColor;RenderSettings.fogDensity=handle.FogDensity;
   RenderSettings.ambientProbe=handle.AmbientProbe;if(handle.Camera)handle.Camera.backgroundColor=handle.CameraBackground;if(handle.Projection)handle.Projection.contentDepth=handle.PreviousProjectionDepth;
   handle.RestoreCameraSettings?.Invoke();handle.RestoreCameraSettings=null;
   foreach(var asset in handle.Assets)if(asset)UnityEngine.Object.Destroy(asset);
   await Task.Yield();foreach(var bundle in handle.Bundles)bundle.Unload(true);handle.Bundles.Clear();handle.Assets.Clear();active=null;
  }
  static Texture2D TextureAt(Texture[] textures,int index)=>index<0?null:textures[index] as Texture2D;
  static Mesh ReadMesh(byte[] bytes,ContentHandle handle){
   using(var r=new BinaryReader(new MemoryStream(bytes))){r.ReadUInt32();int n=r.ReadInt32(),subs=r.ReadInt32();var mesh=new Mesh{indexFormat=n>65535?IndexFormat.UInt32:IndexFormat.UInt16};handle.Assets.Add(mesh);
    var positions=new Vector3[n];var normals=new Vector3[n];var tangents=new Vector4[n];var uv=new Vector2[n];var uv2=new Vector2[n];var colors=new Color[n];
    for(int i=0;i<n;i++){positions[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());normals[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());tangents[i]=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());uv2[i]=new Vector2(r.ReadSingle(),r.ReadSingle());colors[i]=new Color(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
    mesh.vertices=positions;mesh.normals=normals;mesh.tangents=tangents;mesh.uv=uv;mesh.uv2=uv2;mesh.colors=colors;mesh.subMeshCount=subs;
    for(int s=0;s<subs;s++){int count=r.ReadInt32();var indices=new int[count];for(int j=0;j<count;j++)indices[j]=r.ReadInt32();mesh.SetTriangles(indices,s,false);}mesh.RecalculateBounds();return mesh;
   }
  }
  static Texture ReadTexture(byte[] bytes,ContentHandle handle){using(var r=new BinaryReader(new MemoryStream(bytes))){r.ReadUInt32();int w=r.ReadInt32(),h=r.ReadInt32(),format=r.ReadInt32();bool linear=r.ReadInt32()!=0;var wrap=(TextureWrapMode)r.ReadInt32();var filter=(FilterMode)r.ReadInt32();int faces=r.ReadInt32(),mips=r.ReadInt32(),aniso=r.ReadInt32();float bias=r.ReadSingle();
   if(faces==1){if(format==24&&!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H))throw new InvalidDataException("bc6-platform-unsupported");var texture=new Texture2D(w,h,(TextureFormat)format,mips>1,linear){wrapMode=wrap,filterMode=filter,anisoLevel=aniso,mipMapBias=bias};handle.Assets.Add(texture);texture.LoadRawTextureData(r.ReadBytes(bytes.Length-44));texture.Apply(false,true);return texture;}
   var cube=new Cubemap(w,(TextureFormat)format,mips>1){wrapMode=wrap,filterMode=filter,anisoLevel=aniso,mipMapBias=bias};handle.Assets.Add(cube);for(int mip=0;mip<mips;mip++){int count=Math.Max(1,w>>mip)*Math.Max(1,h>>mip)*(format==4?4:8);for(int f=0;f<6;f++)cube.SetPixelData(r.ReadBytes(count),mip,(CubemapFace)f);}cube.Apply(false,true);return cube;
  }}
  static Vector3 Vector3Value(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
  static Vector4 Vector4Value(JToken v)=>new Vector4((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
  static Color ColorValue(JToken v)=>new Color((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
  static Quaternion QuaternionValue(JToken v)=>new Quaternion((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
 }
}

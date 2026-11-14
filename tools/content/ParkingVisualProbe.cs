using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpatialWallpaper.Parking;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk;
public static class ParkingVisualProbe {
 static string output;static Camera camera;static JObject report;static Color[] sourcePixels;
 public static async void Run(){
  output=Environment.GetEnvironmentVariable("CONTENT_VISUAL_OUTPUT");Directory.CreateDirectory(output);report=new JObject{["passed"]=false};
  try{
   EditorSceneManager.OpenScene("Assets/Parking/Scenes/Parking.unity");
   var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Parking/Settings/ParkingURP.asset");GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
   camera=UnityEngine.Object.FindObjectsOfType<Camera>().Single(c=>c.name=="DisplayCamera");camera.enabled=true;camera.tag="MainCamera";camera.allowHDR=true;
   var projection=camera.GetComponent<WindowProjection>();projection.viewportManagedExternally=true;projection.screenFrame=null;projection.displayWidth=.53f;projection.displayHeight=.298125f;projection.contentDepth=15.9f;projection.SetEye(new Vector3(0,0,-.67f));
   var source=UnityEngine.Object.FindObjectOfType<ParkingAtmosphere>();var root=source.gameObject;var field=root.GetComponent<ParkingLampField>();
   typeof(ParkingAtmosphere).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);source.animate=false;source.Evaluate(2.5f);ftLightmaps.RefreshFull();
   var env=root.GetComponent<ParkingEnvironmentBinding>();var sh=new SphericalHarmonicsL2();sh.AddAmbientLight(env.irradiance);RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientProbe=sh;
   report["source_materials"]=new JArray(field.materials.Select(Material));report["source_lightmap_mode"]=LightmapSettings.lightmapsMode.ToString();report["source_lightmaps"]=LightmapSettings.lightmaps.Length;report["source_floor_lightmap"]=source.ground.lightmapIndex;
   var sourceLightObjects=root.GetComponentsInChildren<Light>();var sourceLightStates=sourceLightObjects.Select(l=>l.bakingOutput).ToArray();report["source_lights"]=new JArray(sourceLightObjects.Select(LightInfo));
   var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);var stripping=settings.FindProperty("m_InstancingStripping");report["instancing_stripping"]=stripping.intValue;report["stripping_enum"]=new JArray(stripping.enumNames);
   await Task.Delay(500);Capture("source",()=>typeof(ParkingLampField).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(field,null));
   root.SetActive(false);LightmapSettings.lightmaps=Array.Empty<LightmapData>();
   var loader=new ContentLoader();var package=PackageValidator.Validate(Environment.GetEnvironmentVariable("CONTENT_VISUAL_PACKAGE"));var loaded=await loader.LoadAsync(new SceneLoadRequest{Package=package,Camera=camera,ViewportWidth=.53,ViewportHeight=.298125},CancellationToken.None);
   foreach(var signal in loaded.Root.GetComponentsInChildren<SignalBindings>())signal.Apply(2.5f);
   var imported=loaded.Root.GetComponentInChildren<InstanceField>();report["loaded_materials"]=new JArray(imported.materials.Select(Material));
   report["loaded_lights"]=new JArray(loaded.Root.GetComponentsInChildren<Light>().Select(LightInfo));
   await Task.Delay(500);Capture("imported",()=>Draw(imported));
   var originalShaders=imported.materials.Select(m=>m.shader).ToArray();for(int i=0;i<imported.materials.Length;i++)imported.materials[i].shader=field.materials[i].shader;
   await Task.Delay(100);Capture("imported-source-instance-shaders",()=>Draw(imported));
   for(int i=0;i<imported.materials.Length;i++)imported.materials[i].shader=originalShaders[i];
   var sourceMaterials=imported.materials;imported.materials=field.materials;await Task.Delay(100);Capture("imported-source-instance-materials",()=>Draw(imported));imported.materials=sourceMaterials;
   var floor=loaded.Root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Asphalt");var floorMaterial=floor.sharedMaterial;floor.sharedMaterial=source.ground.sharedMaterial;await Task.Delay(100);Capture("imported-source-floor-material",()=>Draw(imported));floor.sharedMaterial=floorMaterial;
   var host=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Diagnostics/HostPipeline.asset");if(host){QualitySettings.renderPipeline=host;GraphicsSettings.renderPipelineAsset=host;string background=Environment.GetEnvironmentVariable("CONTENT_VISUAL_HOST_BACKGROUND");var rgba=background.Split(',').Select(v=>float.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();camera.backgroundColor=new Color(rgba[0],rgba[1],rgba[2],rgba[3]);await Task.Delay(500);Capture("imported-host-pipeline",()=>Draw(imported));}
   var lights=loaded.Root.GetComponentsInChildren<Light>();foreach(var light in lights){int index=Array.FindIndex(sourceLightObjects,l=>l.name==light.name);light.bakingOutput=sourceLightStates[index];}
   for(int i=0;i<imported.materials.Length;i++)imported.materials[i].shader=field.materials[i].shader;await Task.Delay(100);Capture("imported-source-bake-and-instance-shaders",()=>Draw(imported));
   for(int i=0;i<imported.materials.Length;i++)imported.materials[i].shader=originalShaders[i];
   await loader.UnloadAsync(loaded);report["passed"]=true;if(Environment.GetEnvironmentVariable("CONTENT_VISUAL_VERIFY")=="true")foreach(string capture in new[]{"imported","imported-host-pipeline"})if((int)report["source"]["left_field_bright_pixels"]<900||(double)report[capture]["mean_absolute_rgb_error"]>.0001||(double)report[capture]["maximum_channel_error"]>8.0/255||(int)report[capture]["left_field_bright_pixels"]<(int)report["source"]["left_field_bright_pixels"]*.98)throw new Exception("source-package-appearance-mismatch:"+capture);
  }catch(Exception e){report["passed"]=false;report["error"]=e.ToString();Debug.LogException(e);}
  File.WriteAllText(Path.Combine(output,"visual-probe.json"),report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
 }
 static JObject Material(Material m)=>new JObject{["name"]=m.name,["shader"]=m.shader.name,["instancing"]=m.enableInstancing,["emission"]=m.HasProperty("_LampEmission")?new JArray(m.GetColor("_LampEmission").r,m.GetColor("_LampEmission").g,m.GetColor("_LampEmission").b):null};
 static JObject LightInfo(Light l)=>new JObject{["name"]=l.name,["intensity"]=l.intensity,["range"]=l.range,["bake_type"]=l.bakingOutput.lightmapBakeType.ToString(),["is_baked"]=l.bakingOutput.isBaked,["mixed_mode"]=l.bakingOutput.mixedLightingMode.ToString(),["shadows"]=(int)l.shadows};
 static void Draw(InstanceField field)=>typeof(InstanceField).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(field,null);
 static void Capture(string name,Action draw){
  var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf);target.antiAliasing=4;camera.targetTexture=target;var previous=RenderTexture.active;
  try{draw();camera.Render();RenderTexture.active=target;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());var pixels=image.GetPixels();int bright=0;double sum=0,error=0,max=0;for(int y=100;y<400;y++)for(int x=0;x<350;x++){var c=pixels[y*960+x];sum+=c.grayscale;if(c.maxColorComponent>.7f)bright++;}if(name=="source")sourcePixels=pixels;for(int i=0;i<pixels.Length;i++){var delta=pixels[i]-sourcePixels[i];error+=Math.Abs(delta.r)+Math.Abs(delta.g)+Math.Abs(delta.b);max=Math.Max(max,Math.Max(Math.Abs(delta.r),Math.Max(Math.Abs(delta.g),Math.Abs(delta.b))));}report[name]=new JObject{["left_field_bright_pixels"]=bright,["left_field_mean"]=sum/(300*350),["mean_absolute_rgb_error"]=error/(pixels.Length*3),["maximum_channel_error"]=max};UnityEngine.Object.DestroyImmediate(image);}
  finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
 }
}

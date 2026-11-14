// Original MIT validation geometry. Runs only in the owned test project.
using System;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageGModernLightingVisuals {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static GameObject Shape(Transform parent,Vector3 position,Vector3 scale,Material material){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<MeshRenderer>().sharedMaterial=material;return go;}
 static byte[] Capture(Camera view,string file){
  var target=new RenderTexture(384,216,24,RenderTextureFormat.ARGB32);var image=new Texture2D(384,216,TextureFormat.RGB24,false);var previous=RenderTexture.active;
  try{Need(target.Create(),"lighting-target-create");view.targetTexture=target;var request=new RenderPipeline.StandardRequest{destination=target};for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(view,request);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,384,216),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());return image.GetRawTextureData<byte>().ToArray();}
  finally{view.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
 }
 static int Difference(byte[] a,byte[] b){Need(a.Length==b.Length,"lighting-image-size");int n=0;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])n++;return n;}
 public static JArray Run(string output){
  string folder="Assets/Lighting-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);TrustedRenderProfileBuilder.Install(folder);
  RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.ambientIntensity=0;RenderSettings.reflectionIntensity=0;
  var view=new GameObject("Lighting test camera").AddComponent<Camera>();view.enabled=false;view.transform.position=new Vector3(0,2,-4);view.transform.LookAt(new Vector3(0,.3f,0));view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=Color.black;view.nearClipPlane=.05f;view.farClipPlane=30;view.fieldOfView=50;view.aspect=384f/216;view.allowHDR=true;view.gameObject.AddComponent<UniversalAdditionalCameraData>();
  var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.65f,.4f,.2f,1));material.SetFloat("_Smoothness",0);
  var results=new JArray();
  try{foreach(var type in new[]{LightType.Point,LightType.Spot,LightType.Directional}){
   var root=new GameObject("Lighting visual author root");Shape(root.transform,new Vector3(0,-.1f,0),new Vector3(5,.1f,5),material);Shape(root.transform,new Vector3(0,.5f,0),new Vector3(.7f,1,.7f),material);
   var lamp=new GameObject("Lamp");lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=new Vector3(1.2f,2,-1);lamp.transform.LookAt(Vector3.zero);var source=lamp.AddComponent<Light>();source.type=type;source.lightmapBakeType=LightmapBakeType.Realtime;source.intensity=type==LightType.Directional?1.3f:8;source.range=10;source.spotAngle=90;source.innerSpotAngle=45;source.shadows=LightShadows.Hard;source.shadowBias=.03f;source.shadowNormalBias=.1f;lamp.AddComponent<UniversalAdditionalLightData>().usePipelineSettings=false;
   try{
    var license=new JArray(new JObject{{"name","Original lighting visual fixture"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});var bytes=ScriptSceneExporter.Export(root,new Vector3(6,3.375f,5),license);File.WriteAllBytes(Path.Combine(output,"visual-"+type+".spatial"),bytes);
    var original=Capture(view,Path.Combine(output,type+"-author.png"));source.enabled=false;var dark=Capture(view,Path.Combine(output,type+"-no-light.png"));source.enabled=true;source.shadows=LightShadows.None;var unshadowed=Capture(view,Path.Combine(output,type+"-no-shadow.png"));source.shadows=LightShadows.Hard;
    int lit=Difference(original,dark),shadow=Difference(original,unshadowed);Need(lit>100,"lighting-control-invisible:"+type);Need(shadow>100,"shadow-control-invisible:"+type);var repeated=Capture(view,Path.Combine(output,type+"-author-repeated.png"));int repeatedDifference=Difference(original,repeated);root.SetActive(false);
    using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(Admission.Admit(bytes,"windows-x86_64"),domain)){
     host.Root.SetActive(true);var actual=Capture(view,Path.Combine(output,type+"-admitted.png"));int difference=Difference(original,actual);var delta=new JArray();for(int i=0;i<actual.Length;i++)if(actual[i]!=original[i])delta.Add(new JObject{{"channel_index",i},{"author",original[i]},{"admitted",actual[i]}});
     var actualLight=host.Root.GetComponentInChildren<Light>();File.WriteAllText(Path.Combine(output,type+"-render-diagnostic.json"),new JObject{{"different_channels",delta},{"repeated_author_difference",repeatedDifference},{"author_rotation",source.transform.rotation.ToString("R")},{"admitted_rotation",actualLight.transform.rotation.ToString("R")}}.ToString());
     Need(difference==0,"lighting-render-roundtrip:"+type+":"+difference);
     results.Add(new JObject{{"type",type.ToString()},{"different_rgb_channels",difference},{"light_control_different_channels",lit},{"shadow_control_different_channels",shadow}});
    }
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }}finally{UnityEngine.Object.DestroyImmediate(view.gameObject);UnityEngine.Object.DestroyImmediate(material);}
  return results;
 }
}

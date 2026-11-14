using System;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageGModernLightingChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static JObject Reject(string name,Action<Light> edit,string reason,string output){
  var root=new GameObject("Unsupported light fixture");var light=root.AddComponent<Light>();light.lightmapBakeType=LightmapBakeType.Realtime;Texture2D cookie=null;
  try{
   if(name=="cookie"){light.type=LightType.Spot;cookie=new Texture2D(1,1);light.cookie=cookie;Need(light.cookie==cookie,"cookie-negative-fixture-not-configured");}else edit(light);
   try{ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),new JArray(new JObject{{"name","Original validation fixture"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}}));}
   catch(Exception error){Need(error.ToString().Contains(reason),"unexpected-light-rejection:"+name+":"+error);File.WriteAllText(Path.Combine(output,"rejected-"+name+".txt"),error.ToString());return new JObject{{"case",name},{"reason",reason}};}
   throw new Exception("unsupported-light-was-exported:"+name);
  }finally{UnityEngine.Object.DestroyImmediate(root);if(cookie)UnityEngine.Object.DestroyImmediate(cookie);}
 }
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SDK_G_LIGHT_OUTPUT");
  try{
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var observations=new JArray();
   foreach(var type in new[]{LightType.Point,LightType.Spot,LightType.Directional})foreach(bool pipelineSettings in new[]{true,false}){
    var root=new GameObject("Original modern lighting fixture");var child=new GameObject("Light");child.transform.SetParent(root.transform,false);
    var source=child.AddComponent<Light>();source.type=type;source.color=new Color(.2f,.4f,.6f,1);source.intensity=2.5f;source.range=3.5f;source.spotAngle=50;source.innerSpotAngle=20;source.shadows=LightShadows.Soft;source.shadowBias=.07f;source.shadowNormalBias=.25f;source.shadowNearPlane=.15f;
    source.lightmapBakeType=LightmapBakeType.Realtime;source.shadowStrength=.8f;
    if(!pipelineSettings)child.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().usePipelineSettings=false;
    var license=new JArray(new JObject{{"name","Original lighting validation fixture"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
    byte[] bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);File.WriteAllBytes(Path.Combine(output,type+"-"+pipelineSettings+".spatial"),bytes);var build=Admission.Admit(bytes,"windows-x86_64");
    using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
     var actual=host.Root.GetComponentsInChildren<Light>(true).Single();
     Need(actual.type==source.type,"modern-light-type-roundtrip");Need(actual.color==source.color&&Mathf.Abs(actual.intensity-source.intensity)<.00001f,"modern-light-color-intensity-roundtrip");
     Need(Mathf.Abs(actual.range-source.range)<.00001f&&Mathf.Abs(actual.spotAngle-source.spotAngle)<.00001f&&Mathf.Abs(actual.innerSpotAngle-source.innerSpotAngle)<.00001f,"modern-light-range-spot-roundtrip");
     Need(actual.shadows==source.shadows&&Mathf.Abs(actual.shadowBias-source.shadowBias)<.00001f&&Mathf.Abs(actual.shadowNormalBias-source.shadowNormalBias)<.00001f&&Mathf.Abs(actual.shadowNearPlane-source.shadowNearPlane)<.00001f,"modern-light-shadow-roundtrip");
     Need(Mathf.Abs(actual.shadowStrength-source.shadowStrength)<.00001f&&actual.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().usePipelineSettings==pipelineSettings,"modern-light-shadow-settings-roundtrip");
     observations.Add(new JObject{{"type",type.ToString()},{"pipeline_settings",pipelineSettings},{"properties_preserved",true}});
    }
    UnityEngine.Object.DestroyImmediate(root);
   }
   var rejected=new JArray();
   rejected.Add(Reject("disabled",x=>x.enabled=false,"light-type-or-disabled-not-supported",output));
   rejected.Add(Reject("area",x=>x.type=LightType.Area,"light-type-or-disabled-not-supported",output));
   rejected.Add(Reject("cookie",x=>{},"light-cookie-flare-or-baking-not-supported",output));
   rejected.Add(Reject("baked",x=>x.lightmapBakeType=LightmapBakeType.Baked,"light-cookie-flare-or-baking-not-supported",output));
   rejected.Add(Reject("culling-mask",x=>x.cullingMask=1,"light-render-state-not-supported",output));
   rejected.Add(Reject("bounce",x=>x.bounceIntensity=.5f,"light-unrepresented-state-not-supported",output));
   rejected.Add(Reject("additional-shadow-tier",x=>{var data=x.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();var serialized=new SerializedObject(data);serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=0;serialized.ApplyModifiedPropertiesWithoutUndo();},"light-additional-settings-not-supported",output));
   var rendered=StageGModernLightingVisuals.Run(output);
   File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_G_LIGHT_RUN")},{"cases",observations},{"rejected",rejected},{"rendered",rendered},{"scope","Modern light export/admission/native parameter and Editor render roundtrip with light/shadow controls; not LPAC or physical projection acceptance"}}.ToString());
  }catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}
 }
}

using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk;
// Separate validation binary only. Observes state without changing rendering.
public sealed class ContentLifecycleObserver:MonoBehaviour {
 IEnumerator Start(){
  string path=NaturalSeriesCapture.Argument("--lifecycle-output");Directory.CreateDirectory(Path.GetDirectoryName(path));int tick=0;
  while(true){
   var presentation=FindObjectOfType<RuntimeContentPresentation>();
   if(presentation&&presentation.DisplayCamera){
    var camera=presentation.DisplayCamera;var settings=camera.GetUniversalAdditionalCameraData();var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;var global=Shader.GetGlobalTexture("_ReflectionTexture");
    var targets=Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t=>t&&t.name=="ContentPlanarReflection").ToArray();
    var report=new JObject{["tick"]=++tick,["ready"]=presentation.Ready,["roots"]=FindObjectsOfType<Transform>().Count(t=>t.parent==null&&t.name.StartsWith("Content-")),["planar"]=FindObjectsOfType<PlanarReflection>().Length,["probes"]=FindObjectsOfType<ReflectionProbe>().Length,["reflection_cameras"]=Resources.FindObjectsOfTypeAll<Camera>().Count(c=>c&&c.name=="ContentReflectionCamera"),["targets"]=targets.Length,["target_ids"]=new JArray(targets.Select(t=>t.GetInstanceID())),["global_reflection"]=global?global.GetInstanceID():0,["invert_culling"]=GL.invertCulling,["hidden_renderers"]=FindObjectsOfType<MeshRenderer>().Count(r=>r.forceRenderingOff),["lightmaps"]=LightmapSettings.lightmaps.Length,["viewport_diagnostic"]=DisplayViewport.Diagnostic,["hdr"]=camera.allowHDR,["msaa"]=camera.allowMSAA?pipeline.msaaSampleCount:1,["opaque"]=settings.requiresColorTexture,["depth"]=settings.requiresDepthTexture,["post"]=settings.renderPostProcessing,["renderer"]=Shader.IsKeywordEnabled("_FORWARD_PLUS")?"forward-plus":"forward"};
    report["ambient_mode"]=(int)RenderSettings.ambientMode;report["ambient_colors"]=new JArray(C(RenderSettings.ambientSkyColor),C(RenderSettings.ambientEquatorColor),C(RenderSettings.ambientGroundColor));
    // Publish immutable snapshots. Windows readers need not share DELETE access,
    // and no reader can collide with replacement of an existing destination.
    string next=path+"."+tick.ToString("D8");File.WriteAllText(next+".tmp",report.ToString());File.Move(next+".tmp",next);
   }
   yield return new WaitForSecondsRealtime(.25f);
  }
 }
 static JArray C(Color value)=>new JArray(value.r,value.g,value.b,value.a);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Resources=UnityEngine.Resources;
namespace SpatialWallpaper.Content {
 internal static class ControlledEffects {
  public static void Build(JArray effects,List<GameObject> nodes,Mesh[] meshes,List<Material> materials,ContentHandle handle,Camera camera,float scale){
   if(effects==null)return;var lights=new Dictionary<int,Light>();
   foreach(JObject effect in effects){int node=(int)effect["node"];switch((string)effect["kind"]){
    case "ambient-gradient":RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=(Color)V4(effect["sky"]);RenderSettings.ambientEquatorColor=(Color)V4(effect["equator"]);RenderSettings.ambientGroundColor=(Color)V4(effect["ground"]);break;
    case "ambient":var probe=new SphericalHarmonicsL2();probe.AddAmbientLight((Color)V4(effect["irradiance"]));RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientProbe=probe;break;
    case "background":camera.backgroundColor=(Color)V4(effect["color"]);break;
    case "clip-depth":handle.ContentDepth=(float)effect["depth"];break;
    case "oscillation":var motion=nodes[node].AddComponent<TransformOscillation>();motion.position=Harmonics(effect["position"]);motion.rotation=Harmonics(effect["rotation"]);break;
    case "surface-material":var surface=nodes[node].AddComponent<SurfaceMaterialBinding>();surface.materials=((JArray)effect["materials"]).Select(m=>materials[(int)m]).ToArray();surface.depthPerScale=(float)effect["depthPerScale"];surface.causticsTiling=V4(effect["tiling"]);break;
    case "planar-reflection":var planar=nodes[node].AddComponent<PlanarReflection>();planar.displayCamera=camera;planar.material=materials[(int)effect["material"]];planar.resolutionScale=(float)effect["resolution"];planar.excluded=((JArray)effect["excluded"]).Select(n=>nodes[(int)n].GetComponent<MeshRenderer>()).Cast<Renderer>().ToArray();break;
    case "reflection-probe":var reflectionProbe=nodes[node].AddComponent<ReflectionProbe>();reflectionProbe.mode=ReflectionProbeMode.Realtime;reflectionProbe.refreshMode=ReflectionProbeRefreshMode.OnAwake;reflectionProbe.timeSlicingMode=ReflectionProbeTimeSlicingMode.NoTimeSlicing;reflectionProbe.resolution=128;reflectionProbe.hdr=true;reflectionProbe.clearFlags=ReflectionProbeClearFlags.SolidColor;reflectionProbe.cullingMask=camera.cullingMask;reflectionProbe.size=V3(effect["size"]);reflectionProbe.center=V3(effect["center"]);reflectionProbe.nearClipPlane=(float)effect["near"];reflectionProbe.farClipPlane=(float)effect["far"];reflectionProbe.shadowDistance=(float)effect["shadowDistance"];reflectionProbe.backgroundColor=(Color)V4(effect["background"]);reflectionProbe.intensity=(float)effect["intensity"];reflectionProbe.blendDistance=(float)effect["blend"];reflectionProbe.boxProjection=(bool)effect["box"];reflectionProbe.importance=(int)effect["importance"];break;
    case "camera":
     var previousDefault=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;var pipeline=UnityEngine.Resources.Load<UniversalRenderPipelineAsset>((string)effect["renderer"]=="forward"?ScenePresentationSettings.ForwardResource:ScenePresentationSettings.ForwardPlusResource);if(!pipeline)throw new InvalidOperationException("platform-pipeline-required");
     bool hadSettings=camera.GetComponent<UniversalAdditionalCameraData>();var settings=camera.GetUniversalAdditionalCameraData();bool hdr=camera.allowHDR,msaa=camera.allowMSAA,post=settings.renderPostProcessing;int samples=pipeline.msaaSampleCount;var opaque=settings.requiresColorOption;var depth=settings.requiresDepthOption;
     handle.RestoreCameraSettings=()=>{if(pipeline)pipeline.msaaSampleCount=samples;GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;if(camera){camera.allowHDR=hdr;camera.allowMSAA=msaa;}if(settings){settings.renderPostProcessing=post;settings.requiresColorOption=opaque;settings.requiresDepthOption=depth;if(!hadSettings)UnityEngine.Object.Destroy(settings);}};
     GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;camera.allowHDR=(bool)effect["hdr"];camera.allowMSAA=(int)effect["msaa"]>1;pipeline.msaaSampleCount=(int)effect["msaa"];settings.renderPostProcessing=(bool)effect["post"];settings.requiresColorOption=(bool)effect["opaque"]?CameraOverrideOption.On:CameraOverrideOption.Off;settings.requiresDepthOption=(bool)effect["depth"]?CameraOverrideOption.On:CameraOverrideOption.Off;break;
    case "post":var volume=nodes[node].AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;var profile=ScriptableObject.CreateInstance<VolumeProfile>();handle.Assets.Add(profile);volume.sharedProfile=profile;var bloom=profile.Add<Bloom>(true);bloom.threshold.Override((float)effect["bloom"][0]);bloom.intensity.Override((float)effect["bloom"][1]);bloom.scatter.Override((float)effect["bloom"][2]);var tone=profile.Add<Tonemapping>(true);tone.mode.Override((bool)effect["aces"]?TonemappingMode.ACES:TonemappingMode.None);var color=profile.Add<ColorAdjustments>(true);color.postExposure.Override((float)effect["exposure"]);color.saturation.Override((float)effect["saturation"]);var vignette=profile.Add<Vignette>(true);vignette.intensity.Override((float)effect["vignette"][0]);vignette.smoothness.Override((float)effect["vignette"][1]);foreach(var component in profile.components)handle.Assets.Add(component);break;
    case "instances":var field=nodes[node].AddComponent<InstanceField>();field.displayCamera=camera;field.mesh=meshes[(int)effect["mesh"]];field.materials=((JArray)effect["materials"]).Select(v=>materials[(int)v]).ToArray();field.positions=((JArray)effect["positions"]).Select(V3).ToArray();field.instanceScale=(float)effect["scale"];break;
    case "light":var light=nodes[node].AddComponent<Light>();light.cullingMask=effect["mask"]!=null?(int)effect["mask"]:-1;light.type=(string)effect["type"]=="point"?LightType.Point:(string)effect["type"]=="spot"?LightType.Spot:LightType.Directional;light.color=(Color)V4(effect["color"]);light.intensity=(float)effect["intensity"]*(light.type==LightType.Directional?1:scale*scale);light.range=(float)effect["range"]*scale;light.spotAngle=(float)effect["spot"];light.innerSpotAngle=(float)effect["innerSpot"];light.shadows=(LightShadows)(int)effect["shadows"];light.shadowBias=(float)effect["bias"];light.shadowNormalBias=(float)effect["normalBias"];light.shadowNearPlane=(float)effect["near"]*scale;var sizing=nodes[node].AddComponent<ContentLightScale>();sizing.Source=light;sizing.Root=handle.Root.transform;sizing.Range=(float)effect["range"];sizing.Near=(float)effect["near"];sizing.Intensity=(float)effect["intensity"];lights.Add(node,light);break;
   }}
   foreach(JObject effect in effects)if((string)effect["kind"]=="signal"){
    var signal=nodes[(int)effect["node"]].AddComponent<SignalBindings>();signal.kind=(string)effect["signal"]=="flicker"?SignalKind.Flicker:SignalKind.Intermittent;signal.seed=(int)effect["seed"];signal.scaleRoot=handle.Root.transform;signal.authorScale=1;
    signal.materials=((JArray)effect["materials"]).OfType<JObject>().Select(b=>new MaterialSignalBinding{material=materials[(int)b["material"]],property=(string)b["property"],kind=(string)b["type"]=="vector-w"?MaterialSignalKind.VectorW:(string)b["type"]=="color"?MaterialSignalKind.Color:(string)b["type"]=="time"?MaterialSignalKind.Time:MaterialSignalKind.Float,basis=V4(b["basis"]),gain=(float)b["gain"]}).ToArray();
    signal.lights=((JArray)effect["lights"]).OfType<JObject>().Select(b=>new LightSignalBinding{light=lights[(int)b["node"]],intensity=(float)b["intensity"]}).ToArray();
    foreach(var binding in signal.lights)binding.light.GetComponent<ContentLightScale>().SignalControlled=true;
   }
  }
  static Vector3 V3(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);static Vector4 V4(JToken v)=>new Vector4((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
  static TransformHarmonic[] Harmonics(JToken value)=>((JArray)value).OfType<JObject>().Select(h=>new TransformHarmonic{amplitude=V3(h["amplitude"]),frequency=V3(h["frequency"]),phase=V3(h["phase"])}).ToArray();
 }
 internal sealed class ContentLightScale:MonoBehaviour {
  public Light Source;public Transform Root;public float Range,Near,Intensity;public bool SignalControlled;
  void LateUpdate(){if(!Source||!Root)return;float s=Root.lossyScale.x;Source.range=Range*s;Source.shadowNearPlane=Near*s;if(!SignalControlled)Source.intensity=Intensity*(Source.type==LightType.Directional?1:s*s);}
 }
}

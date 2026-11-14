using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Sdk.Hosting {
 // Reads actual trusted Unity objects. Never substitutes catalog values when
 // an internal member of the pinned URP version is unavailable.
 public static class UnityRenderObservation {
  const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static object Field(object owner,string name){var field=owner.GetType().GetField(name,Members);if(field==null)throw new InvalidDataException("render-observation-field:"+name);return field.GetValue(owner);}
  static object Property(object owner,string name){var property=owner.GetType().GetProperty(name,Members);if(property==null)throw new InvalidDataException("render-observation-property:"+name);return property.GetValue(owner);}
  public sealed class Binding {
   readonly object pipeline,renderer,data,camera,additional,target;
   internal Binding(object p,object r,object d,object c,object a,object t){pipeline=p;renderer=r;data=d;camera=c;additional=a;target=t;}
   public void RequireSame(Binding other,bool initializingRenderer=false){if(other==null||!ReferenceEquals(pipeline,other.pipeline)||(!initializingRenderer&&!ReferenceEquals(renderer,other.renderer))||!ReferenceEquals(data,other.data)||!ReferenceEquals(camera,other.camera)||!ReferenceEquals(additional,other.additional)||!ReferenceEquals(target,other.target))throw new InvalidDataException("render-profile-object-binding-changed");}
  }
  public static JObject Read(Camera camera,RenderTexture target,TextureFormat readback,out Binding binding){
   if(!camera||!target||!target.IsCreated()||camera.targetTexture!=target)throw new InvalidDataException("render-observation-target-binding");
   var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
   if(!pipeline)throw new InvalidDataException("render-observation-pipeline");
   var additional=camera.GetComponent<UniversalAdditionalCameraData>();
   if(!additional)throw new InvalidDataException("render-observation-camera-data");
   int index=(int)Field(additional,"m_RendererIndex"),selected=index<0?(int)Field(pipeline,"m_DefaultRendererIndex"):index;
   var renderers=(ScriptableRendererData[])Field(pipeline,"m_RendererDataList");
   if(selected<0||selected>=renderers.Length||!(renderers[selected] is UniversalRendererData data))throw new InvalidDataException("render-observation-renderer-data");
   var renderer=additional.scriptableRenderer;
   if(renderer==null)throw new InvalidDataException("render-observation-renderer");
   if(!ReferenceEquals(renderer,pipeline.GetRenderer(selected)))throw new InvalidDataException("render-observation-renderer-binding");
   binding=new Binding(pipeline,renderer,data,camera,additional,target);
   var features=new JArray();foreach(var feature in data.rendererFeatures){if(!feature)throw new InvalidDataException("render-observation-missing-feature");features.Add(new JObject{{"type",feature.GetType().FullName},{"active",feature.isActive}});}
   float distance=pipeline.shadowDistance;
   if(float.IsNaN(distance)||float.IsInfinity(distance)||distance!=Math.Floor(distance))throw new InvalidDataException("render-observation-shadow-distance");
   var settings=new JObject{{"rendering_mode",Property(renderer,"renderingModeActual").ToString()},
    {"hdr",pipeline.supportsHDR},{"msaa",pipeline.msaaSampleCount},{"depth_texture",pipeline.supportsCameraDepthTexture},{"opaque_texture",pipeline.supportsCameraOpaqueTexture},
    {"shadow_cascades",pipeline.shadowCascadeCount},{"shadow_distance",(int)distance},{"main_light_shadows",pipeline.supportsMainLightShadows},
    {"additional_lights",pipeline.additionalLightsRenderingMode.ToString()},{"additional_light_shadows",pipeline.supportsAdditionalLightShadows},{"soft_shadows",pipeline.supportsSoftShadows}};
   return new JObject{{"engine",Application.unityVersion},{"color_space",QualitySettings.activeColorSpace.ToString()},{"graphics_api",SystemInfo.graphicsDeviceType.ToString()},
    {"pipeline_type",pipeline.GetType().FullName},{"renderer_type",renderer.GetType().FullName},{"post_process_data",data.postProcessData!=null},
    {"settings",settings},{"renderer_features",features},
    {"camera",new JObject{{"allow_hdr",camera.allowHDR},{"allow_msaa",camera.allowMSAA},{"post_processing",additional.renderPostProcessing},{"renderer_index",index},{"resolved_renderer_index",selected},{"render_type",additional.renderType.ToString()},{"camera_stack_count",((System.Collections.IList)Field(additional,"m_Cameras")).Count}}},
    {"capture",new JObject{{"format",target.format.ToString()},{"graphics_format",target.graphicsFormat.ToString()},{"depth_stencil_format",target.depthStencilFormat.ToString()},{"srgb",target.sRGB},{"samples",target.antiAliasing},{"depth_bits",target.depth},{"readback",readback.ToString()}}}};
  }
  public static JObject Hardware(RenderTexture target)=>new JObject{{"graphics_device_name",SystemInfo.graphicsDeviceName},{"graphics_device_vendor",SystemInfo.graphicsDeviceVendor},
   {"graphics_device_id",SystemInfo.graphicsDeviceID},{"graphics_device_vendor_id",SystemInfo.graphicsDeviceVendorID},{"graphics_device_version",SystemInfo.graphicsDeviceVersion},
   {"graphics_format",target.graphicsFormat.ToString()},{"depth_stencil_format",target.depthStencilFormat.ToString()},{"width",target.width},{"height",target.height}};
 }
}

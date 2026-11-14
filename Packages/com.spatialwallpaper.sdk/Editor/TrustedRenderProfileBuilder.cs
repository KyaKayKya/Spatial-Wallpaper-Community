using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 // Normalized SDK packages use this entry point. The legacy schema 2 scene
 // exporter and its presentation settings retain their existing behavior.
 public static class TrustedRenderProfileBuilder {
  public static UniversalRenderPipelineAsset Create(JObject identity,out UniversalRendererData renderer){
   var profile=RenderProfiles.Require(identity);var settings=(JObject)profile["settings"];
   var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly);
   if(Application.unityVersion!=(string)profile["engine"]||package==null||package.version!=(string)profile["pipeline"])throw new InvalidDataException("render-profile-build-toolchain");
   if(((JArray)profile["renderer_features"]).Count!=0)throw new InvalidDataException("render-profile-feature-not-implemented");
   renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
   renderer.renderingMode=(RenderingMode)Enum.Parse(typeof(RenderingMode),(string)settings["rendering_mode"]);
   var serialized=new SerializedObject(renderer);
   var post=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
   if(!post)throw new InvalidDataException("render-profile-post-process-data-missing");
   serialized.FindProperty("postProcessData").objectReferenceValue=post;serialized.ApplyModifiedPropertiesWithoutUndo();
   var pipeline=UniversalRenderPipelineAsset.Create(renderer);
   pipeline.supportsHDR=(bool)settings["hdr"];pipeline.msaaSampleCount=(int)settings["msaa"];
   pipeline.supportsCameraDepthTexture=(bool)settings["depth_texture"];pipeline.supportsCameraOpaqueTexture=(bool)settings["opaque_texture"];
   pipeline.shadowCascadeCount=(int)settings["shadow_cascades"];pipeline.shadowDistance=(float)settings["shadow_distance"];
   var properties=new SerializedObject(pipeline);
   properties.FindProperty("m_MainLightShadowsSupported").boolValue=(bool)settings["main_light_shadows"];
   properties.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)(LightRenderingMode)Enum.Parse(typeof(LightRenderingMode),(string)settings["additional_lights"]);
   properties.FindProperty("m_AdditionalLightShadowsSupported").boolValue=(bool)settings["additional_light_shadows"];
   properties.FindProperty("m_SoftShadowsSupported").boolValue=(bool)settings["soft_shadows"];properties.ApplyModifiedPropertiesWithoutUndo();
   return pipeline;
  }
  public static JObject Install(string folder){
   var identity=RenderProfiles.DefaultIdentity;var profile=RenderProfiles.Require(identity);
   var pipeline=Create(identity,out var renderer);
   AssetDatabase.CreateAsset(renderer,folder+"/Renderer.asset");AssetDatabase.CreateAsset(pipeline,folder+"/Pipeline.asset");
   GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
   PlayerSettings.colorSpace=(ColorSpace)Enum.Parse(typeof(ColorSpace),(string)profile["color_space"]);
   PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
   PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{(GraphicsDeviceType)Enum.Parse(typeof(GraphicsDeviceType),(string)profile["graphics_api"])});
   AssetDatabase.SaveAssets();
   return new JObject{{"identity",identity},{"engine",Application.unityVersion},{"pipeline",UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly).version},
    {"pipeline_asset_sha256",SpatialWallpaper.Content.PackageValidator.Digest(File.ReadAllBytes(folder+"/Pipeline.asset"))},
    {"renderer_asset_sha256",SpatialWallpaper.Content.PackageValidator.Digest(File.ReadAllBytes(folder+"/Renderer.asset"))}};
  }
 }
}

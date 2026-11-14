using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Sdk.Editor {
 // The same fixed platform profiles build external Shader variants and the
 // Player. Author pipeline assets and RendererFeatures never enter the package.
 public static class PlatformPipelineProfile {
  public static UniversalRenderPipelineAsset Create(PresentationProfile profile,out UniversalRendererData renderer){
   if(!Enum.IsDefined(typeof(PresentationProfile),profile))throw new ArgumentException("unsupported-presentation-profile");
   renderer=ScriptableObject.CreateInstance<UniversalRendererData>();renderer.renderingMode=profile==PresentationProfile.Forward?RenderingMode.Forward:RenderingMode.ForwardPlus;
   var data=new SerializedObject(renderer);data.FindProperty("postProcessData").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");data.ApplyModifiedPropertiesWithoutUndo();
   var pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.supportsHDR=true;pipeline.msaaSampleCount=4;pipeline.shadowCascadeCount=1;pipeline.supportsCameraDepthTexture=true;pipeline.supportsCameraOpaqueTexture=true;
   var settings=new SerializedObject(pipeline);settings.FindProperty("m_MainLightShadowsSupported").boolValue=true;settings.FindProperty("m_AdditionalLightsRenderingMode").intValue=1;settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue=profile==PresentationProfile.ForwardPlus;settings.FindProperty("m_SoftShadowsSupported").boolValue=true;settings.FindProperty("m_ShadowDistance").floatValue=12;settings.ApplyModifiedPropertiesWithoutUndo();return pipeline;
  }
  internal static IDisposable ShaderBuildScope(PresentationProfile profile)=>new Scope(profile);
  sealed class Scope:IDisposable {
   readonly RenderPipelineAsset previousDefault,previousQuality;readonly int quality;readonly string folder;bool disposed;
   public Scope(PresentationProfile profile){previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;quality=QualitySettings.GetQualityLevel();string name="SpatialShaderExport-"+Guid.NewGuid().ToString("N");folder="Assets/"+name;
    try{AssetDatabase.CreateFolder("Assets",name);var pipeline=Create(profile,out var renderer);AssetDatabase.CreateAsset(renderer,folder+"/Renderer.asset");AssetDatabase.CreateAsset(pipeline,folder+"/Pipeline.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;AssetDatabase.SaveAssets();}
    catch{Dispose();throw;}
   }
   public void Dispose(){if(disposed)return;disposed=true;GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.SetQualityLevel(quality,false);QualitySettings.renderPipeline=previousQuality;if(AssetDatabase.IsValidFolder(folder))AssetDatabase.DeleteAsset(folder);}
  }
 }
}

// Adapted from eldskald/planar-reflections-unity (MIT, Rafael Bordoni 2022,
// URP port Marcell Hermanowski), through the existing Pool adapter.
// See ThirdParty/PlanarReflections-LICENSE.txt.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Sdk {
 public sealed class PlanarReflection:MonoBehaviour {
  public Material material;public Renderer[] excluded=Array.Empty<Renderer>();public float resolutionScale=.5f;[NonSerialized]public Camera displayCamera;
  Camera reflection;RenderTexture target;Texture previousGlobal,previousOutput;bool rendering;bool[] previousHidden;
  void OnEnable(){previousGlobal=Shader.GetGlobalTexture("_ReflectionTexture");previousOutput=material?material.GetTexture("_ReflectionTex"):null;previousHidden=new bool[excluded.Length];RenderPipelineManager.beginCameraRendering+=Render;}
  void Render(ScriptableRenderContext context,Camera source){
   if(rendering||!displayCamera||source!=displayCamera)return;
   int width=Mathf.Max(64,Mathf.RoundToInt(source.pixelWidth*resolutionScale)),height=Mathf.Max(64,Mathf.RoundToInt(source.pixelHeight*resolutionScale));if((long)width*height>4194304)throw new InvalidOperationException("reflection-target-budget");
   if(!reflection){var go=new GameObject("ContentReflectionCamera"){hideFlags=HideFlags.HideAndDontSave};reflection=go.AddComponent<Camera>();reflection.enabled=false;var data=go.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=false;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;}
   if(!target||target.width!=width||target.height!=height){ReleaseTarget();target=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf){name="ContentPlanarReflection",hideFlags=HideFlags.HideAndDontSave,useMipMap=true,autoGenerateMips=true};if(!target.Create())throw new InvalidOperationException("reflection-allocation-failed");}
   reflection.CopyFrom(source);reflection.enabled=false;reflection.cameraType=CameraType.Reflection;reflection.targetTexture=target;reflection.GetUniversalAdditionalCameraData().renderPostProcessing=false;reflection.backgroundColor=new Color(.003f,.025f,.022f);
   float heightY=transform.position.y;var mirror=Matrix4x4.identity;mirror.m11=-1;mirror.m13=2*heightY;reflection.transform.position=mirror.MultiplyPoint(source.transform.position);reflection.worldToCameraMatrix=source.worldToCameraMatrix*mirror;reflection.projectionMatrix=source.projectionMatrix;
   var point=reflection.worldToCameraMatrix.MultiplyPoint(new Vector3(0,heightY-.001f,0));var normal=reflection.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;reflection.projectionMatrix=reflection.CalculateObliqueMatrix(new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(point,normal)));
   bool invert=GL.invertCulling;rendering=true;
   try{for(int i=0;i<excluded.Length;i++){previousHidden[i]=excluded[i].forceRenderingOff;excluded[i].forceRenderingOff=true;}GL.invertCulling=!invert;UniversalRenderPipeline.RenderSingleCamera(context,reflection);material.SetTexture("_ReflectionTex",target);Shader.SetGlobalTexture("_ReflectionTexture",target);}
   finally{GL.invertCulling=invert;for(int i=0;i<excluded.Length;i++)if(excluded[i])excluded[i].forceRenderingOff=previousHidden[i];rendering=false;}
  }
  void ReleaseTarget(){if(!target)return;if(material&&material.GetTexture("_ReflectionTex")==target)material.SetTexture("_ReflectionTex",previousOutput);if(Shader.GetGlobalTexture("_ReflectionTexture")==target)Shader.SetGlobalTexture("_ReflectionTexture",previousGlobal);target.Release();Destroy(target);target=null;}
  void OnDisable(){RenderPipelineManager.beginCameraRendering-=Render;ReleaseTarget();if(reflection){Destroy(reflection.gameObject);reflection=null;}}
 }
}

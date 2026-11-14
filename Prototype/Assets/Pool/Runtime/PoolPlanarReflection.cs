// Adapted from eldskald/planar-reflections-unity (MIT, Rafael Bordoni 2022,
// URP port Marcell Hermanowski). See Assets/ThirdParty/PlanarReflections/LICENSE.txt.
// Adaptation: explicit off-axis matrix, one display camera, bounded resolution,
// water exclusion, exception-safe culling, and owned texture destruction.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;

public sealed class PoolPlanarReflection:MonoBehaviour {
 public Material reflectionMaterial;
 public float resolutionScale=.5f;
 public bool useSourceBackground;
 public bool mipFilteredReflection;
 public bool reflectBothSides;
 public Material radianceSplitMaterial;
 public LayerMask excludedLayers;
 Camera reflection;
 RenderTexture target;
 RenderTexture detailTarget,highlightTarget;
 bool rendering;
 public static Matrix4x4 Mirror(float y){var m=Matrix4x4.identity;m.m11=-1;m.m13=2*y;return m;}
 void OnEnable(){RenderPipelineManager.beginCameraRendering+=Render;}
 void Start(){
  var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--pool-quality");
  if(i>=0&&i+1<args.Length){if(args[i+1]=="low")resolutionScale=.25f;if(args[i+1]=="static"){reflectionMaterial.SetFloat("_Strength",0);enabled=false;}}
 }
 void Render(ScriptableRenderContext context,Camera source){
  if(rendering||source.cameraType!=CameraType.Game||!source.CompareTag("MainCamera"))return;
  if(!reflection){
   var go=new GameObject("PoolReflectionCamera"){hideFlags=HideFlags.HideAndDontSave};reflection=go.AddComponent<Camera>();reflection.enabled=false;
   var data=go.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=false;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
  }
  int w=Mathf.Max(64,Mathf.RoundToInt(source.pixelWidth*resolutionScale)),h=Mathf.Max(64,Mathf.RoundToInt(source.pixelHeight*resolutionScale));
  if(!target||target.width!=w||target.height!=h){ReleaseTarget();target=new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf){name="PoolPlanarReflection",hideFlags=HideFlags.HideAndDontSave,useMipMap=true,autoGenerateMips=true,filterMode=mipFilteredReflection?FilterMode.Trilinear:FilterMode.Bilinear,anisoLevel=mipFilteredReflection?4:1};target.Create();}
  reflection.CopyFrom(source);reflection.enabled=false;reflection.cameraType=CameraType.Reflection;reflection.targetTexture=target;reflection.cullingMask=source.cullingMask&~((1<<4)|excludedLayers.value);
  reflection.GetUniversalAdditionalCameraData().renderPostProcessing=false;
  reflection.backgroundColor=useSourceBackground?source.backgroundColor:new Color(.003f,.025f,.022f);
  float height=transform.position.y;
  var mirror=Mirror(height);reflection.transform.position=mirror.MultiplyPoint(source.transform.position);
  reflection.worldToCameraMatrix=source.worldToCameraMatrix*mirror;
  reflection.projectionMatrix=source.projectionMatrix;
  float side=reflectBothSides&&source.transform.position.y<height?-1:1;
  // An exactly coplanar eye otherwise makes the oblique projection singular.
  float offset=reflectBothSides?Mathf.Max(.00001f,Mathf.Min(.001f,Mathf.Abs(source.transform.position.y-height)*.25f)):.001f;
  var point=reflection.worldToCameraMatrix.MultiplyPoint(new Vector3(0,height-side*offset,0));
  var normal=reflection.worldToCameraMatrix.MultiplyVector(Vector3.up*side).normalized;
  reflection.projectionMatrix=reflection.CalculateObliqueMatrix(new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(point,normal)));
  bool old=GL.invertCulling;rendering=true;
  try{
   GL.invertCulling=!old;UniversalRenderPipeline.RenderSingleCamera(context,reflection);
   reflectionMaterial.SetTexture("_ReflectionTex",target);
   if(radianceSplitMaterial){
    if(!detailTarget){detailTarget=SplitTarget(w,h,"LimpoolReflectionDetail");highlightTarget=SplitTarget(w,h,"LimpoolReflectionHighlights");}
    var cmd=CommandBufferPool.Get("Limpool reflection radiance split");
    try{cmd.Blit(target,detailTarget,radianceSplitMaterial,0);cmd.Blit(target,highlightTarget,radianceSplitMaterial,1);cmd.GenerateMips(detailTarget);cmd.GenerateMips(highlightTarget);context.ExecuteCommandBuffer(cmd);}
    finally{CommandBufferPool.Release(cmd);}
   }
   Shader.SetGlobalTexture("_ReflectionTexture",radianceSplitMaterial?detailTarget:target);
   Shader.SetGlobalTexture("_ReflectionHighlights",radianceSplitMaterial?(Texture)highlightTarget:Texture2D.blackTexture);
   Shader.SetGlobalTexture("_ReflectionRaw",target);
   Shader.SetGlobalFloat("_ReflectionRadianceSplit",radianceSplitMaterial?1:0);
   Shader.SetGlobalVector("_ReflectionTexture_TexelSize",new Vector4(1f/w,1f/h,w,h));
  }
  finally{GL.invertCulling=old;rendering=false;}
 }
 RenderTexture SplitTarget(int w,int h,string name){var rt=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf){name=name,hideFlags=HideFlags.HideAndDontSave,useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Trilinear,anisoLevel=1};rt.Create();return rt;}
 void ReleaseTarget(){if(target){if(reflectionMaterial)reflectionMaterial.SetTexture("_ReflectionTex",null);if(Shader.GetGlobalTexture("_ReflectionTexture")==target||Shader.GetGlobalTexture("_ReflectionTexture")==detailTarget){Shader.SetGlobalTexture("_ReflectionTexture",Texture2D.blackTexture);Shader.SetGlobalTexture("_ReflectionHighlights",Texture2D.blackTexture);Shader.SetGlobalTexture("_ReflectionRaw",Texture2D.blackTexture);Shader.SetGlobalFloat("_ReflectionRadianceSplit",0);}target.Release();Destroy(target);target=null;}if(detailTarget){detailTarget.Release();Destroy(detailTarget);detailTarget=null;}if(highlightTarget){highlightTarget.Release();Destroy(highlightTarget);highlightTarget=null;}}
 void OnDisable(){RenderPipelineManager.beginCameraRendering-=Render;ReleaseTarget();if(reflection){Destroy(reflection.gameObject);reflection=null;}}
}

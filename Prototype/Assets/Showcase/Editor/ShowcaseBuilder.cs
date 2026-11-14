using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
public static class ShowcaseBuilder {
 const string Base="Assets/Showcase/";
 public static string Evidence{get{return Path.GetFullPath(Path.Combine(Application.dataPath,"../../Validation/Showcase/unity"));}}
 static T Asset<T>(string path) where T:UnityEngine.Object{return AssetDatabase.LoadAssetAtPath<T>(Base+path);}
 static Material Paint(string name,string hex,float smooth){
  var m=Asset<Material>("Materials/"+name+".mat");
  if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,Base+"Materials/"+name+".mat");}
  Color c;ColorUtility.TryParseHtmlString(hex,out c);m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",0);EditorUtility.SetDirty(m);return m;
 }
 static T Setting<T>(string file) where T:ScriptableObject{
  var a=Asset<T>("Settings/"+file);if(!a){a=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(a,Base+"Settings/"+file);}return a;
 }
 static void ImportAssets(){
  foreach(var name in new[]{"Cabinet.fbx","Organic/OrganicSculpture.fbx"}){
   var imp=(ModelImporter)AssetImporter.GetAtPath(Base+"Models/"+name);
   if(imp==null)throw new Exception("Missing exported model: "+name);
   imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.isReadable=false;imp.generateSecondaryUV=name.StartsWith("Cabinet");
   imp.importCameras=false;imp.importLights=false;imp.SaveAndReimport();
  }
  foreach(var name in new[]{"Albedo","Normal","MetallicSmoothness"}){
   var imp=(TextureImporter)AssetImporter.GetAtPath(Base+"Models/Organic/"+name+".png");
   imp.textureType=name=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
   imp.sRGBTexture=false;imp.isReadable=false;imp.mipmapEnabled=true;imp.maxTextureSize=1024;
   imp.textureCompression=TextureImporterCompression.CompressedHQ;imp.SaveAndReimport();
  }
 }
 static void Pipeline(){
  var rd=Setting<UniversalRendererData>("ShowcaseForward.asset");
  rd.renderingMode=RenderingMode.Forward;rd.intermediateTextureMode=IntermediateTextureMode.Auto;
  var rso=new SerializedObject(rd);rso.FindProperty("postProcessData").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");rso.ApplyModifiedPropertiesWithoutUndo();
  if(rd.rendererFeatures.Count==0){
   var type=typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
   var ao=(ScriptableRendererFeature)ScriptableObject.CreateInstance(type);ao.name="ContactOcclusion";
   AssetDatabase.AddObjectToAsset(ao,rd);rd.rendererFeatures.Add(ao);
  }
  var so=new SerializedObject(rd.rendererFeatures[0]);
  so.FindProperty("m_Settings.Downsample").boolValue=true;so.FindProperty("m_Settings.Intensity").floatValue=.6f;
  so.FindProperty("m_Settings.Radius").floatValue=.012f;so.FindProperty("m_Settings.DirectLightingStrength").floatValue=.5f;
  so.ApplyModifiedPropertiesWithoutUndo();rd.SetDirty();EditorUtility.SetDirty(rd);
  var urp=Asset<UniversalRenderPipelineAsset>("Settings/ShowcaseURP.asset");
  if(!urp){urp=UniversalRenderPipelineAsset.Create(rd);AssetDatabase.CreateAsset(urp,Base+"Settings/ShowcaseURP.asset");}
  urp.supportsHDR=true;urp.msaaSampleCount=2;urp.renderScale=1;urp.supportsCameraDepthTexture=false;urp.supportsCameraOpaqueTexture=false;urp.useSRPBatcher=true;
  urp.shadowCascadeCount=1;urp.shadowDistance=1.6f;urp.shadowDepthBias=.03f;urp.shadowNormalBias=.08f;
  so=new SerializedObject(urp);
  so.FindProperty("m_MainLightShadowsSupported").boolValue=true;so.FindProperty("m_MainLightShadowmapResolution").intValue=1024;
  so.FindProperty("m_SoftShadowsSupported").boolValue=true;so.FindProperty("m_AdditionalLightShadowsSupported").boolValue=false;
  so.FindProperty("m_AdditionalLightsRenderingMode").intValue=0;so.FindProperty("m_ReflectionProbeBoxProjection").boolValue=true;
  so.FindProperty("m_ReflectionProbeBlending").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
  GraphicsSettings.defaultRenderPipeline=urp;QualitySettings.renderPipeline=urp;EditorUtility.SetDirty(urp);
 }
 public static string Create(){
  foreach(var folder in new[]{"Materials","Settings","Scenes","Prefabs"})Directory.CreateDirectory(Base+folder);
  Directory.CreateDirectory(Evidence);ImportAssets();Pipeline();
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
  PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;
  PlayerSettings.companyName="Spatial Wallpaper";PlayerSettings.productName="Spatial Window Preview";PlayerSettings.enableFrameTimingStats=true;
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var white=Paint("WarmWhitePaint","#E6E3DC",.34f);var backing=Paint("MatteBacking","#E0DDD6",.27f);
  var ceramic=Paint("IvoryCeramic","#FFFFFF",1);
  ceramic.SetTexture("_BaseMap",Asset<Texture2D>("Models/Organic/Albedo.png"));
  ceramic.SetTexture("_BumpMap",Asset<Texture2D>("Models/Organic/Normal.png"));ceramic.SetFloat("_BumpScale",.28f);ceramic.EnableKeyword("_NORMALMAP");
  ceramic.SetTexture("_MetallicGlossMap",Asset<Texture2D>("Models/Organic/MetallicSmoothness.png"));ceramic.EnableKeyword("_METALLICSPECGLOSSMAP");
  var frame=new GameObject("DisplayWindow_XY_Z0");
  var cabinet=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>("Models/Cabinet.fbx"));
  cabinet.name="FixedCabinet";cabinet.transform.SetParent(frame.transform,false);cabinet.transform.localRotation=Quaternion.Euler(0,180,0);
  cabinet.transform.localScale=new Vector3(1,.99375f,1);
  foreach(var r in cabinet.GetComponentsInChildren<MeshRenderer>()){
   r.sharedMaterial=r.name.Contains("Back")?backing:white;
   GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.ContributeGI|StaticEditorFlags.BatchingStatic|StaticEditorFlags.ReflectionProbeStatic);
   r.receiveGI=ReceiveGI.Lightmaps;
  }
  // Interior orientation A can be applied to content without rotating the display aperture.
  var content=new GameObject("InteriorContent_A");content.transform.SetParent(frame.transform,false);
  var slot=new GameObject("ReplaceableSlot");slot.transform.SetParent(content.transform,false);slot.transform.localPosition=new Vector3(0,-.087f*.99375f,.095f);
  var prop=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>("Models/Organic/OrganicSculpture.fbx"));
  prop.name="OrganicSculpture";prop.transform.SetParent(slot.transform,false);prop.transform.localRotation=Quaternion.Euler(0,180,0)*prop.transform.localRotation;
  foreach(var r in prop.GetComponentsInChildren<MeshRenderer>()){
   r.sharedMaterial=ceramic;r.lightProbeUsage=LightProbeUsage.BlendProbes;r.reflectionProbeUsage=ReflectionProbeUsage.Simple;
   GameObjectUtility.SetStaticEditorFlags(r.gameObject,0);
  }
  PrefabUtility.SaveAsPrefabAsset(slot,Base+"Prefabs/ReplaceableOrganic.prefab");
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.backgroundColor=new Color(.72f,.7f,.66f);
  camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=true;
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;
  var driver=camera.gameObject.AddComponent<PreviewEyeDriver>();driver.projection=projection;driver.cadence=cadence;
  var bench=camera.gameObject.AddComponent<ShowcaseBenchmark>();bench.projection=projection;bench.cadence=cadence;
  var profile=Setting<VolumeProfile>("DisplayAppearance.asset");
  // Remove stale missing components, and persist new components as subassets.
  profile.components.RemoveAll(x=>!x);
  Tonemapping tm;if(!profile.TryGet(out tm)){tm=profile.Add<Tonemapping>(true);AssetDatabase.AddObjectToAsset(tm,profile);}tm.mode.Override(TonemappingMode.Neutral);
  ColorAdjustments ca;if(!profile.TryGet(out ca)){ca=profile.Add<ColorAdjustments>(true);AssetDatabase.AddObjectToAsset(ca,profile);}ca.postExposure.Override(.2f);
  EditorUtility.SetDirty(profile);EditorUtility.SetDirty(tm);EditorUtility.SetDirty(ca);
  var volume=new GameObject("DisplayTone").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
  var key=new GameObject("RealtimeSculptureKey").AddComponent<Light>();key.type=LightType.Directional;key.transform.rotation=Quaternion.LookRotation(new Vector3(.26f,-.25f,.34f));
  key.color=new Color(1,.95f,.87f);key.intensity=.65f;key.bounceIntensity=1;key.lightmapBakeType=LightmapBakeType.Mixed;
  key.shadows=LightShadows.Soft;key.shadowBias=.03f;key.shadowNormalBias=.08f;RenderSettings.sun=key;
  var softbox=new GameObject("BakedStudioSoftbox").AddComponent<Light>();softbox.type=LightType.Area;
  softbox.transform.position=new Vector3(-.24f,.205f,-.22f);softbox.transform.LookAt(new Vector3(.02f,-.045f,.12f));
  softbox.areaSize=new Vector2(.10f,.08f);softbox.color=new Color(1,.91f,.78f);softbox.intensity=7;softbox.lightmapBakeType=LightmapBakeType.Baked;
  RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.42f);
  RenderSettings.reflectionIntensity=.5f;RenderSettings.defaultReflectionResolution=128;
  var group=new GameObject("EmptyCabinetLightProbes").AddComponent<LightProbeGroup>();var points=new List<Vector3>();
  foreach(float x in new[]{-.20f,0,.20f})foreach(float y in new[]{-.078f,.03f,.13f})foreach(float z in new[]{.02f,.17f})points.Add(new Vector3(x,y,z));
  group.probePositions=points.ToArray();
  var probe=new GameObject("EmptyCabinetReflection").AddComponent<ReflectionProbe>();probe.transform.position=new Vector3(0,0,.095f);
  probe.size=new Vector3(.53f,.298125f,.19f);probe.boxProjection=true;probe.resolution=128;probe.mode=ReflectionProbeMode.Baked;
  probe.clearFlags=ReflectionProbeClearFlags.SolidColor;probe.backgroundColor=new Color(.35f,.38f,.42f);probe.nearClipPlane=.005f;probe.farClipPlane=2;
  var ls=Asset<LightingSettings>("Settings/EmptyCabinetLighting.lighting"); if(!ls){ls=new LightingSettings();AssetDatabase.CreateAsset(ls,Base+"Settings/EmptyCabinetLighting.lighting");}
  ls.bakedGI=true;ls.realtimeGI=false;ls.autoGenerate=false;ls.mixedBakeMode=MixedLightingMode.IndirectOnly;
  ls.lightmapResolution=256;ls.lightmapMaxSize=512;ls.directionalityMode=LightmapsMode.NonDirectional;ls.lightmapper=LightingSettings.Lightmapper.ProgressiveCPU;
  ls.directSampleCount=64;ls.indirectSampleCount=256;ls.environmentSampleCount=128;ls.maxBounces=4;
  ls.ao=true;ls.aoMaxDistance=.025f;ls.aoExponentIndirect=.6f;ls.lightmapPadding=4;Lightmapping.lightingSettings=ls;EditorUtility.SetDirty(ls);
  EditorSceneManager.SaveScene(scene,Base+"Scenes/Showcase.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Base+"Scenes/Showcase.unity",true)};
  AssetDatabase.SaveAssets();return "Created accepted Organic scene. "+ShowcaseChecks.Run();
 }
 public static string Bake(){
  GameObject.Find("ReplaceableSlot").SetActive(false);EditorSceneManager.SaveOpenScenes();
  Lightmapping.bakeCompleted-=FinishBake;Lightmapping.bakeCompleted+=FinishBake;
  if(!Lightmapping.BakeAsync()){FinishBake();throw new Exception("Bake did not start");}return "Baking cabinet with sculpture disabled";
 }
 public static void FinishBake(){
  Lightmapping.bakeCompleted-=FinishBake;
  foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
   foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name=="ReplaceableSlot")t.gameObject.SetActive(true);
  EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
 }
 public static string Capture(string name,Vector3 eye,int width=1600,int height=900){
  Directory.CreateDirectory(Evidence);
  var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();var oldEye=projection.EyeScreen;projection.SetEye(eye);var cam=projection.GetComponent<Camera>();
  var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.antiAliasing=2;
  var old=cam.targetTexture;var active=RenderTexture.active;var rect=cam.rect;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
  try{
   cam.rect=new Rect(0,0,1,1);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
   tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),tex.EncodeToPNG());
  }finally{cam.targetTexture=old;cam.rect=rect;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);projection.SetEye(oldEye);}
  return name;
 }
 public static string Build(){
  Directory.CreateDirectory("Builds/Showcase");EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
  var report=BuildPipeline.BuildPlayer(new[]{Base+"Scenes/Showcase.unity"},"Builds/Showcase/Showcase.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  return report.summary.result+" bytes="+report.summary.totalSize+" errors="+report.summary.totalErrors;
 }
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VolumetricLights;
using ShadowResolution = VolumetricLights.ShadowResolution;

public static class LimpoolBuilder {
 const string R="Assets/Limpool/";const float S=.53f/8;
 [Serializable] public class Item {public string name;public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;public bool rail;}
 [Serializable] public class Source {public Item[] items;}
 static void Save(UnityEngine.Object o,string p){AssetDatabase.CreateAsset(o,R+p);}
 static Material Mat(string name,string shader){var m=new Material(Shader.Find(shader));m.name=name;Save(m,"Materials/"+name+".mat");return m;}
 public static void RepairLeftJoint(){
  var go=GameObject.Find("Rear_Left_Window_Sill");var mesh=go.GetComponent<MeshFilter>().sharedMesh;
  var vertices=mesh.vertices;bool changed=false;
  for(int i=0;i<vertices.Length;i++)if(vertices[i].x < -3.8f && Mathf.Abs(vertices[i].x+4.02f)>.00001f){vertices[i].x=-4.02f;changed=true;}
  if(!changed)return;
  mesh.vertices=vertices;mesh.RecalculateBounds();mesh.RecalculateTangents();Unwrapping.GenerateSecondaryUVSet(mesh);EditorUtility.SetDirty(mesh);
  go.GetComponent<MeshCollider>().sharedMesh=null;go.GetComponent<MeshCollider>().sharedMesh=mesh;
 }
 public static void Create(){
  foreach(var p in new[]{"Materials","Meshes","Settings","Scenes"})Directory.CreateDirectory(R+p);
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  foreach(var dir in new[]{"Materials","Meshes","Settings"})foreach(var guid in AssetDatabase.FindAssets("",new[]{R+dir})){var path=AssetDatabase.GUIDToAssetPath(guid);if(!AssetDatabase.IsValidFolder(path))AssetDatabase.DeleteAsset(path);}
  foreach(var path in Directory.GetFiles(R+"Generated").Where(x=>x.EndsWith(".jpg")||x.EndsWith(".png"))){var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.textureType=path.Contains("nor_gl")?TextureImporterType.NormalMap:TextureImporterType.Default;ti.maxTextureSize=2048;ti.mipmapEnabled=true;ti.SaveAndReimport();}
  var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();renderer.name="LimpoolForward";Save(renderer,"Settings/Forward.asset");
  var rs=new SerializedObject(renderer);rs.FindProperty("postProcessData").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");rs.ApplyModifiedPropertiesWithoutUndo();
  var urp=UniversalRenderPipelineAsset.Create(renderer);urp.name="LimpoolURP";urp.supportsHDR=true;urp.msaaSampleCount=2;urp.supportsCameraDepthTexture=true;urp.supportsCameraOpaqueTexture=true;urp.useSRPBatcher=true;urp.shadowDistance=2;urp.shadowCascadeCount=1;urp.shadowDepthBias=.01f;urp.shadowNormalBias=.02f;
  var us=new SerializedObject(urp);us.FindProperty("m_MainLightShadowsSupported").boolValue=true;us.FindProperty("m_MainLightShadowmapResolution").intValue=2048;us.FindProperty("m_SoftShadowsSupported").boolValue=true;us.ApplyModifiedPropertiesWithoutUndo();Save(urp,"Settings/LimpoolURP.asset");
  var frame=new GameObject("DisplayWindow_XY_Z0");var content=new GameObject("LimpoolContent");content.transform.SetParent(frame.transform,false);content.AddComponent<SceneRenderSettings>().pipeline=urp;content.AddComponent<DisplaySceneVolume>().size=new Vector3(.53f,.298125f,1.6f);
  var metres=new GameObject("AuthoredMetres");metres.transform.SetParent(content.transform,false);metres.transform.localScale=Vector3.one*S;metres.transform.localPosition=new Vector3(0,-2.25f*S,0);
  var architecture=new GameObject("LimpoolArchitecture");architecture.transform.SetParent(metres.transform,false);
  var plaster=Mat("NeutralPlaster","Universal Render Pipeline/Lit");plaster.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(R+"Generated/Wall_Neutral_BaseColor.png"));plaster.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(R+"Generated/beige_wall_001_nor_gl_4k.jpg"));plaster.EnableKeyword("_NORMALMAP");plaster.SetFloat("_BumpScale",.32f);plaster.SetFloat("_Smoothness",.18f);plaster.SetColor("_BaseColor",new Color(.7f,.72f,.72f));
  var rail=Mat("SatinRails","Universal Render Pipeline/Lit");rail.SetColor("_BaseColor",new Color(.48f,.53f,.54f));rail.SetFloat("_Metallic",.25f);rail.SetFloat("_Smoothness",.42f);
  var waterLights=new GameObject("DynamicWaterLightReceivers");waterLights.transform.SetParent(metres.transform,false);
  var glow=Mat("WaterLight","SpatialWallpaper/LimpoolWaterLight");glow.SetTexture("_Caustics",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Pool/Textures/Caustics.tif"));glow.SetFloat("_Strength",.45f);
  foreach(var item in JsonUtility.FromJson<Source>(File.ReadAllText(R+"Generated/architecture.json")).items){
   var mesh=new Mesh{name=item.name,vertices=item.vertices,normals=item.normals,uv=item.uv,triangles=item.triangles};mesh.RecalculateTangents();mesh.RecalculateBounds();Unwrapping.GenerateSecondaryUVSet(mesh);Save(mesh,"Meshes/"+item.name+".asset");
   var go=new GameObject(item.name);go.transform.SetParent(architecture.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=item.rail?rail:plaster;go.AddComponent<MeshCollider>().sharedMesh=mesh;GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.ContributeGI);mr.receiveGI=ReceiveGI.Lightmaps;
   if(!item.rail){var receiver=new GameObject(item.name+"_WaterLight");receiver.transform.SetParent(waterLights.transform,false);receiver.AddComponent<MeshFilter>().sharedMesh=mesh;var rr=receiver.AddComponent<MeshRenderer>();rr.sharedMaterial=glow;rr.shadowCastingMode=ShadowCastingMode.Off;}
  }
  var water=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/URPWater/Demo/Materials/Water/M_URPWater_Demo_RealTime.mat"));water.name="LimpoolWater";Save(water,"Materials/Water.mat");
  water.SetColor("_Color",new Color(.08f,.32f,.35f));water.SetColor("_DepthColor",new Color(.008f,.045f,.075f));water.SetFloat("_DepthStart",0);water.SetFloat("_DepthEnd",.19f);water.SetFloat("_Distortion",2.5f);water.SetFloat("_Smoothness",.86f);
  water.SetFloat("_NormalMapAIntensity",.36f);water.SetVector("_NormalMapATilings",new Vector4(8,16,13,23));water.SetVector("_NormalMapASpeeds",new Vector4(.12f,.06f,-.07f,.09f));water.SetFloat("_ReflectionIntensity",.8f);water.SetFloat("_ReflectionDistortion",.18f);water.SetFloat("_ReflectionRoughness",.08f);water.SetFloat("_ReflectionFresnel",3.5f);
  foreach(var keyword in new[]{"_CAUSTICS_ON","_FOAM_ON","_FOAM_RIPPLE_ON","_FOAM_WHITECAPS_ON","_DISPLACEMENTMODE_GERSTNER","_SPEED_MUL_ON"})water.DisableKeyword(keyword);
  water.SetFloat("_SpeedMul",0);water.SetFloat("_Caustics",0);water.SetFloat("_Foam",0);water.SetFloat("_ZWrite",1);water.EnableKeyword("_ZWRITE_ON");
  var wm=new Mesh{name="WaterCoverage",vertices=new[]{new Vector3(-60,1.1f,-16),new Vector3(-60,1.1f,100),new Vector3(60,1.1f,100),new Vector3(60,1.1f,-16)},uv=new[]{new Vector2(0,0),new Vector2(0,58),new Vector2(60,58),new Vector2(60,0)},triangles=new[]{0,1,2,0,2,3}};wm.RecalculateNormals();wm.RecalculateTangents();Save(wm,"Meshes/Water.asset");
  var surface=new GameObject("WaterSurface");surface.layer=4;surface.transform.SetParent(metres.transform,false);surface.AddComponent<MeshFilter>().sharedMesh=wm;surface.AddComponent<MeshRenderer>().sharedMaterial=water;surface.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
  // Reflection component expects transform origin on the mirror plane.
  surface.transform.localPosition=Vector3.up*1.1f;wm.vertices=wm.vertices.Select(v=>v-Vector3.up*1.1f).ToArray();wm.RecalculateBounds();EditorUtility.SetDirty(wm);
  var refl=surface.AddComponent<PoolPlanarReflection>();refl.reflectionMaterial=Mat("ReflectionBinding","SpatialWallpaper/PoolReflection");
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.32f,.43f);camera.allowHDR=true;camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.contentDepth=8;projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;var preview=camera.gameObject.AddComponent<PreviewEyeDriver>();preview.projection=projection;preview.cadence=cadence;preview.showHelp=false;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=content.transform;
  var bench=camera.gameObject.AddComponent<ShowcaseBenchmark>();bench.projection=projection;bench.cadence=cadence;content.AddComponent<ContinuousRenderDemand>();camera.gameObject.AddComponent<LimpoolCapture>();
  var sun=new GameObject("LimpoolSun").AddComponent<Light>();sun.transform.SetParent(content.transform,false);sun.type=LightType.Directional;sun.transform.rotation=Quaternion.LookRotation(new Vector3(-.8f,-.57f,-.1f));sun.intensity=1.7f;sun.color=new Color(1,.91f,.8f);sun.shadows=LightShadows.Soft;sun.shadowBias=.0002f;sun.shadowNormalBias=.001f;RenderSettings.sun=sun;
  RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.12f,.19f,.24f);RenderSettings.ambientEquatorColor=new Color(.055f,.095f,.12f);RenderSettings.ambientGroundColor=new Color(.025f,.055f,.07f);
  var beamGo=new GameObject("SkylightVolume");beamGo.transform.SetParent(content.transform,false);beamGo.transform.position=new Vector3(3*S,(4.65f-2.25f)*S,7.2f*S);beamGo.transform.rotation=sun.transform.rotation;
  var lamp=beamGo.AddComponent<Light>();lamp.type=LightType.Spot;lamp.range=1;lamp.spotAngle=27;lamp.intensity=0;lamp.enabled=false;
  var beam=beamGo.AddComponent<VolumetricLight>();beam.alwaysOn=true;beam.brightnessMode=BrightnessMode.Absolute;beam.brightness=.5f;beam.density=1.8f;beam.mediumAlbedo=new Color(.8f,.88f,1);beam.enableShadows=true;beam.shadowIntensity=1;beam.shadowNearDistance=.001f;beam.shadowCullingMask=1;beam.shadowResolution=ShadowResolution._1024;beam.shadowBakeInterval=ShadowBakeInterval.OnStart;beam.useNoise=true;beam.noiseScale=7;beam.noiseStrength=.5f;beam.windDirection=new Vector3(.001f,.002f,0);beam.enableDustParticles=false;beam.renderQueue=3101;beam.raymarchQuality=32;beam.raymarchMaxSteps=96;beam.jittering=.1f;beam.targetCamera=camera.transform;beam.Refresh();
  var effects=content.AddComponent<LimpoolEffects>();effects.water=water;effects.reflection=refl;effects.waterLights=waterLights;effects.beam=beam;
  var vp=ScriptableObject.CreateInstance<VolumeProfile>();Save(vp,"Settings/Appearance.asset");var tone=vp.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);AssetDatabase.AddObjectToAsset(tone,vp);var volume=new GameObject("Appearance").AddComponent<Volume>();volume.transform.SetParent(content.transform,false);volume.isGlobal=true;volume.sharedProfile=vp;
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;PlayerSettings.enableFrameTimingStats=true;
  EditorSceneManager.SaveScene(scene,R+"Scenes/Limpool.unity");AssetDatabase.SaveAssets();
 }
 public static void Build(){
  EditorSceneManager.OpenScene(R+"Scenes/Limpool.unity");
  RepairLeftJoint();
  var beam=GameObject.Find("SkylightVolume").GetComponent<VolumetricLight>();beam.transform.rotation=GameObject.Find("LimpoolSun").transform.rotation;beam.Refresh();
  var water=AssetDatabase.LoadAssetAtPath<Material>(R+"Materials/Water.mat");water.SetVector("_NormalMapATilings",new Vector4(1.3f,1.7f,2.1f,2.6f));water.SetFloat("_NormalMapAIntensity",.5f);EditorUtility.SetDirty(water);
  water.SetColor("_Color",new Color(.35f,.78f,.8f));water.SetColor("_DepthColor",new Color(.045f,.22f,.3f));water.SetFloat("_DepthEnd",.45f);water.SetFloat("_ReflectionIntensity",.65f);water.SetFloat("_ReflectionDistortion",.55f);water.SetFloat("_Smoothness",.8f);
  water.shader=Shader.Find("SpatialWallpaper/LimpoolWater");water.shaderKeywords=Array.Empty<string>();water.SetFloat("_ReflectionIntensity",.8f);
  UnityEngine.Object.FindObjectOfType<PoolPlanarReflection>().useSourceBackground=true;
  UnityEngine.Object.FindObjectOfType<PoolPlanarReflection>().mipFilteredReflection=true;
  UnityEngine.Object.FindObjectOfType<PoolPlanarReflection>().excludedLayers=1<<30;
  foreach(var t in beam.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  LimpoolWaterMask.Build();
  UnityEngine.Object.FindObjectOfType<ShowcaseBenchmark>().demoEyeVerticalScale=.9f;
  if(!GameObject.Find("PoolBottomContinuation")){
   var bottom=GameObject.CreatePrimitive(PrimitiveType.Cube);bottom.name="PoolBottomContinuation";bottom.transform.SetParent(GameObject.Find("AuthoredMetres").transform,false);
   bottom.transform.localPosition=new Vector3(0,-.08f,54);bottom.transform.localScale=new Vector3(120,.16f,92);
   bottom.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(R+"Materials/NeutralPlaster.mat");
  }
  var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(R+"Settings/Appearance.asset");
  if(!profile.TryGet<ColorAdjustments>(out var color)){color=profile.Add<ColorAdjustments>(true);AssetDatabase.AddObjectToAsset(color,profile);}color.postExposure.Override(.8f);EditorUtility.SetDirty(color);EditorUtility.SetDirty(profile);
  var rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(R+"Settings/Forward.asset");
  // Water refraction must read current-frame opaque depth, not last frame's
  // after-transparent depth (which already includes the water surface).
  rd.copyDepthMode=CopyDepthMode.AfterOpaques;EditorUtility.SetDirty(rd);
  if(!rd.rendererFeatures.OfType<VolumetricLightsDepthPrePassFeature>().Any()){
   var feature=ScriptableObject.CreateInstance<VolumetricLightsDepthPrePassFeature>();feature.name="WaterDepthForVolumes";feature.transparentLayerMask=1<<4;feature.Create();rd.rendererFeatures.Add(feature);AssetDatabase.AddObjectToAsset(feature,rd);EditorUtility.SetDirty(rd);
  }
  // The optimized plugin override clips at the near plane. Use its documented
  // own-shader path so transparent depth and the clamped water surface agree.
  foreach(var depth in rd.rendererFeatures.OfType<VolumetricLightsDepthPrePassFeature>()){
   depth.useOptimizedDepthOnlyShader=false;EditorUtility.SetDirty(depth);
  }
  LimpoolLook.Apply();
  var coverage=AssetDatabase.LoadAssetAtPath<Mesh>(R+"Meshes/Water.asset");
  coverage.vertices=new[]{new Vector3(-1500,0,-16),new Vector3(-1500,0,1500),new Vector3(1500,0,1500),new Vector3(1500,0,-16)};coverage.RecalculateBounds();EditorUtility.SetDirty(coverage);
  UnityEngine.Object.FindObjectOfType<WindowProjection>().contentDepth=110;
  EditorSceneManager.SaveOpenScenes();
  var old=GraphicsSettings.defaultRenderPipeline;GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(R+"Settings/LimpoolURP.asset");AssetDatabase.SaveAssets();Directory.CreateDirectory("Builds/Limpool");
  var report=BuildPipeline.BuildPlayer(new[]{R+"Scenes/Limpool.unity","Assets/Train/Scenes/Train.unity"},"Builds/Limpool/Limpool.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  GraphicsSettings.defaultRenderPipeline=old;AssetDatabase.SaveAssets();File.WriteAllText("../Validation/Limpool/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Build failed");
 }
}

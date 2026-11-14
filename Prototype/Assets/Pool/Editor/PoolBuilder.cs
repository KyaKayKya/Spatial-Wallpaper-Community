using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class PoolBuilder {
 const string Root="Assets/Pool/";
 const float Scale=.53f/11.2f;
 const float Centre=1.65f;
 static T Load<T>(string path) where T:UnityEngine.Object{return AssetDatabase.LoadAssetAtPath<T>(Root+path);}
 static void Save(UnityEngine.Object asset,string path){AssetDatabase.CreateAsset(asset,Root+path);}
 static Material Mat(string name,string shader){var m=new Material(Shader.Find(shader)){name=name};Save(m,"Materials/"+name+".mat");return m;}
 static Mesh Grid(string name,int nx,int nz,float width,float depth){
  var v=new Vector3[(nx+1)*(nz+1)];var uv=new Vector2[v.Length];var tri=new int[nx*nz*6];int k=0;
  for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++){int i=z*(nx+1)+x;v[i]=new Vector3((float)x/nx*width-width/2,0,(float)z/nz*depth);uv[i]=new Vector2((float)x/nx,(float)z/nz);}
  for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int a=z*(nx+1)+x,b=a+nx+1;tri[k++]=a;tri[k++]=b;tri[k++]=a+1;tri[k++]=a+1;tri[k++]=b;tri[k++]=b+1;}
  var mesh=new Mesh{name=name,vertices=v,uv=uv,triangles=tri};mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();Save(mesh,"Models/"+name+".asset");return mesh;
 }
 public static void Create(){
  foreach(var p in new[]{"Materials","Settings","Scenes","Prefabs","Models"})Directory.CreateDirectory(Root+p);
  // Rebuilding only replaces this task's generated assets, preserving source GUIDs.
  foreach(var p in new[]{"Materials","Settings","Prefabs"})foreach(var guid in AssetDatabase.FindAssets("",new[]{Root+p})){var path=AssetDatabase.GUIDToAssetPath(guid);if(!AssetDatabase.IsValidFolder(path))AssetDatabase.DeleteAsset(path);}
  foreach(var p in new[]{"Models/WaterGrid.asset","Models/FrontGrid.asset"})if(Load<Mesh>(p))AssetDatabase.DeleteAsset(Root+p);
  var imp=(ModelImporter)AssetImporter.GetAtPath(Root+"Baked/pool_baked.fbx");
  imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.importLights=false;imp.importCameras=false;imp.generateSecondaryUV=false;imp.isReadable=true;imp.SaveAndReimport();
  foreach(var path in Directory.GetFiles(Root+"Baked","*.exr")){
   var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.sRGBTexture=false;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=1024;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
  }
  foreach(var path in Directory.GetFiles(Root+"Textures").Where(p=>p.EndsWith(".png")||p.EndsWith(".tif"))){
   var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.textureType=path.Contains("normal")?TextureImporterType.NormalMap:TextureImporterType.Default;
   ti.sRGBTexture=path.Contains("basecolor");ti.mipmapEnabled=true;ti.maxTextureSize=2048;ti.SaveAndReimport();
  }
  var rd=ScriptableObject.CreateInstance<UniversalRendererData>();rd.name="PoolForward";
  var rso=new SerializedObject(rd);rso.FindProperty("postProcessData").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");rso.ApplyModifiedPropertiesWithoutUndo();Save(rd,"Settings/PoolForward.asset");
  var urp=UniversalRenderPipelineAsset.Create(rd);urp.name="PoolURP";urp.supportsHDR=true;urp.msaaSampleCount=2;urp.supportsCameraDepthTexture=true;urp.supportsCameraOpaqueTexture=true;urp.useSRPBatcher=true;urp.shadowDistance=2;urp.shadowCascadeCount=1;urp.shadowDepthBias=.01f;urp.shadowNormalBias=.02f;
  var so=new SerializedObject(urp);so.FindProperty("m_MainLightShadowsSupported").boolValue=true;so.FindProperty("m_MainLightShadowmapResolution").intValue=2048;so.FindProperty("m_SoftShadowsSupported").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();Save(urp,"Settings/PoolURP.asset");
  GraphicsSettings.defaultRenderPipeline=urp;QualitySettings.renderPipeline=urp;
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var frame=new GameObject("DisplayWindow_XY_Z0");var content=new GameObject("PoolContent");content.transform.SetParent(frame.transform,false);
  content.AddComponent<SceneRenderSettings>().pipeline=urp;
  var geometry=new GameObject("PoolMetres");geometry.transform.SetParent(content.transform,false);geometry.transform.localScale=Vector3.one*Scale;geometry.transform.localPosition=new Vector3(0,-Centre*Scale,0);
  var model=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>("Baked/pool_baked.fbx"));
  var importedRotation=model.transform.localRotation;
  PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
  model.name="PoolArchitecture";model.transform.SetParent(geometry.transform,false);model.transform.localRotation=Quaternion.Euler(0,180,0)*importedRotation;
  var tiles=Mat("CeramicAndCaustics","SpatialWallpaper/PoolCeramic");tiles.SetTexture("_BaseMap",Load<Texture2D>("Textures/ivory_tiles_basecolor.png"));tiles.SetTexture("_BumpMap",Load<Texture2D>("Textures/ivory_tiles_normal.png"));tiles.SetTexture("_Roughness",Load<Texture2D>("Textures/ivory_tiles_roughness.png"));tiles.SetFloat("_WaterLevel",-Centre*Scale);
  var vinyl=Mat("RingVinyl","Universal Render Pipeline/Lit");vinyl.SetTexture("_BaseMap",Load<Texture2D>("Textures/ring_retro_basecolor.png"));vinyl.SetFloat("_Smoothness",.82f);
  Transform ring=null;
  foreach(var r in model.GetComponentsInChildren<MeshRenderer>()){
   bool isRing=r.name.IndexOf("ring",StringComparison.OrdinalIgnoreCase)>=0;
   if(isRing){r.sharedMaterial=vinyl;ring=r.transform;}
   else {var m=new Material(tiles){name=r.name+" Baked"};m.SetTexture("_DiffuseBake",Load<Texture2D>("Baked/"+r.name+".exr"));m.SetTexture("_Caustics",Load<Texture2D>("Textures/Caustics.tif"));Save(m,"Materials/"+r.name+".mat");r.sharedMaterial=m;}
   r.shadowCastingMode=ShadowCastingMode.TwoSided;
  }
  ring.SetParent(geometry.transform,true);
  var water=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/URPWater/Demo/Materials/Water/M_URPWater_Demo_RealTime.mat")){name="PoolWater"};Save(water,"Materials/PoolWater.mat");
  water.SetFloat("_DepthStart",0);water.SetFloat("_DepthEnd",.12f);water.SetFloat("_Distortion",8);
  water.SetColor("_Color",new Color(.12f,.38f,.32f,1));water.SetColor("_DepthColor",new Color(.006f,.075f,.065f,1));
  water.SetFloat("_NormalMapAIntensity",.22f);water.SetFloat("_ReflectionIntensity",1);water.SetFloat("_ReflectionFresnel",2.5f);water.SetFloat("_ReflectionDistortion",.12f);
  water.SetVector("_NormalMapATilings",new Vector4(5,4,7,6));water.SetVector("_NormalMapASpeeds",new Vector4(.08f,.05f,-.05f,-.07f));
  water.SetFloat("_SpeedMul",1);water.SetFloat("_FoamRipples",0);water.DisableKeyword("_FOAM_RIPPLE_ON");
  water.SetFloat("_CausticsIntensity",.5f);water.SetFloat("_CausticsEnd",.12f);
  var surface=new GameObject("WaterSurface");surface.transform.SetParent(geometry.transform,false);surface.AddComponent<MeshFilter>().sharedMesh=Grid("WaterGrid",80,64,11.2f,9);surface.AddComponent<MeshRenderer>().sharedMaterial=water;surface.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
  surface.layer=4;
  var reflectMat=Mat("WaterReflection","SpatialWallpaper/PoolReflection");
  reflectMat.SetTexture("_WaveNormal",Load<Texture2D>("Textures/Caustics.tif"));
  var reflected=new GameObject("WaterReflection");reflected.layer=4;reflected.transform.SetParent(geometry.transform,false);reflected.transform.localPosition=Vector3.up*.012f;
  reflected.AddComponent<MeshFilter>().sharedMesh=surface.GetComponent<MeshFilter>().sharedMesh;reflected.AddComponent<MeshRenderer>().sharedMaterial=reflectMat;reflected.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
  reflected.GetComponent<MeshRenderer>().enabled=false;
  var planar=surface.AddComponent<PoolPlanarReflection>();planar.reflectionMaterial=reflectMat;
  var frontMat=Mat("CutawayTint","SpatialWallpaper/PoolWater");frontMat.SetFloat("_AbsorptionScale",.4f);frontMat.renderQueue=3002;
  var front=new GameObject("WaterFront");front.transform.SetParent(geometry.transform,false);front.transform.localRotation=Quaternion.Euler(90,0,0);front.AddComponent<MeshFilter>().sharedMesh=Grid("FrontGrid",1,1,11.2f,1.5f);front.AddComponent<MeshRenderer>().sharedMaterial=frontMat;front.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(3,2.2f,1.2f);camera.allowHDR=true;
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;
  var preview=camera.gameObject.AddComponent<PreviewEyeDriver>();preview.projection=projection;preview.cadence=cadence;preview.exhibit=ring.gameObject;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=content.transform;
  var bench=camera.gameObject.AddComponent<ShowcaseBenchmark>();bench.projection=projection;bench.cadence=cadence;
  var demand=content.AddComponent<ContinuousRenderDemand>();var animation=content.AddComponent<PoolAnimation>();animation.ring=ring;animation.water=water;animation.tiles=tiles;animation.demand=demand;
  content.AddComponent<PoolMaterialState>().waterSurface=surface.transform;
  var sun=new GameObject("PoolSun").AddComponent<Light>();sun.transform.SetParent(content.transform,false);sun.type=LightType.Directional;sun.transform.rotation=Quaternion.LookRotation(new Vector3(-.469f,-.292f,.834f));sun.color=new Color(1,.78f,.49f);sun.intensity=2.2f;sun.shadows=LightShadows.Soft;sun.shadowBias=.0005f;sun.shadowNormalBias=.001f;RenderSettings.sun=sun;
  RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.32f,.38f,.44f);RenderSettings.ambientEquatorColor=new Color(.2f,.23f,.22f);RenderSettings.ambientGroundColor=new Color(.1f,.14f,.13f);RenderSettings.reflectionIntensity=1;
  var probe=new GameObject("PoolReflection").AddComponent<ReflectionProbe>();probe.transform.SetParent(content.transform,false);probe.transform.position=new Vector3(0,0,.2f);probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.NoTimeSlicing;probe.resolution=128;probe.size=Vector3.one*2;probe.nearClipPlane=.005f;probe.farClipPlane=2;probe.clearFlags=ReflectionProbeClearFlags.SolidColor;probe.backgroundColor=new Color(.4f,.5f,.6f);
  var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="PoolAppearance";Save(profile,"Settings/PoolAppearance.asset");
  var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);AssetDatabase.AddObjectToAsset(tone,profile);
  var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.22f);bloom.threshold.Override(1.2f);bloom.scatter.Override(.6f);AssetDatabase.AddObjectToAsset(bloom,profile);
  var volume=new GameObject("PoolTone").AddComponent<Volume>();volume.transform.SetParent(content.transform,false);volume.isGlobal=true;volume.sharedProfile=profile;
  // The interchangeable asset is Pool.unity: its lightmaps are scene-owned.
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;PlayerSettings.enableFrameTimingStats=true;
  EditorSceneManager.SaveScene(scene,Root+"Scenes/Pool.unity");AssetDatabase.SaveAssets();
  File.WriteAllText("../Validation/Pool/build-geometry.txt",string.Join("\n",model.GetComponentsInChildren<MeshRenderer>().Select(r=>r.name+" "+r.bounds)));
  var showcase=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Showcase/Settings/ShowcaseURP.asset");
  GraphicsSettings.defaultRenderPipeline=showcase;QualitySettings.renderPipeline=showcase;AssetDatabase.SaveAssets();
 }
 public static void BuildBatch(){
  try{Create();Directory.CreateDirectory("Builds/Pool");
   // URP gathers build variants from Graphics + Quality, not scene component refs.
   // Keep Pool as Graphics default and Showcase as the Quality override in the Player.
   GraphicsSettings.defaultRenderPipeline=Load<UniversalRenderPipelineAsset>("Settings/PoolURP.asset");
   AssetDatabase.SaveAssets();
   var report=BuildPipeline.BuildPlayer(new[]{Root+"Scenes/Pool.unity","Assets/Showcase/Scenes/Showcase.unity"},"Builds/Pool/Pool.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
   GraphicsSettings.defaultRenderPipeline=QualitySettings.renderPipeline;AssetDatabase.SaveAssets();
   File.WriteAllText("../Validation/Pool/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
   EditorApplication.Exit(report.summary.totalErrors==0&&report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded?0:1);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
 }
}

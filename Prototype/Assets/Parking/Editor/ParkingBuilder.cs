using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using SpatialWallpaper.Parking;
public static class ParkingBuilder {
 const string Root="Assets/Parking/",Gen=Root+"Generated/";
 const float Scale=.53f/12f;
 const float LampScale=1f;
 static Dictionary<string,Material> mats;
 static Dictionary<string,Mesh> meshes;
 static Dictionary<string,Material[]> meshMats;
 static Vector3 V(JToken t){return new Vector3((float)t[0],(float)t[1],(float)t[2]);}
 static string Safe(string s){return System.Text.RegularExpressions.Regex.Replace(s,"[^A-Za-z0-9_-]","_");}
 static void Save(UnityEngine.Object a,string path){var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);if(old){EditorUtility.CopySerialized(a,old);UnityEngine.Object.DestroyImmediate(a);}else AssetDatabase.CreateAsset(a,path);}
 static Material Make(string name,string shader){var m=new Material(Shader.Find(shader)){name=name,enableInstancing=true};return m;}
 static Texture2D Tex(string name){return AssetDatabase.LoadAssetAtPath<Texture2D>(Gen+name);}
 static void Import(){
  foreach(string path in Directory.GetFiles(Gen,"*.png")){
   var imp=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));if(!imp)continue;
   bool normal=path.ToLowerInvariant().Contains("normal")||path.Contains("nor_gl");
   imp.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
   imp.sRGBTexture=!normal&&!path.ToLowerInvariant().Contains("rough")&&!path.ToLowerInvariant().Contains("metal")&&!path.Contains("Weathering");
   imp.maxTextureSize=path.Contains("clean_asphalt")?4096:2048;imp.anisoLevel=8;imp.filterMode=FilterMode.Trilinear;imp.textureCompression=TextureImporterCompression.CompressedHQ;imp.mipmapEnabled=true;imp.isReadable=false;imp.SaveAndReimport();
  }
 }
 static void Materials(JArray data){
  mats=new Dictionary<string,Material>();
  foreach(var d in data){
   string name=(string)d["name"];var m=Make(name,"Universal Render Pipeline/Lit");
   // Blender socket values are scene-linear; Unity Color shader properties are stored as sRGB.
   m.SetColor("_BaseColor",new Color((float)d["color"][0],(float)d["color"][1],(float)d["color"][2]).gamma);
   m.SetFloat("_Smoothness",Mathf.Clamp01(1-(float)d["roughness"]));m.SetFloat("_Metallic",(float)d["metallic"]);
   foreach(var t in (JArray)d["textures"]){
    string n=((string)t["name"]).ToLowerInvariant();var texture=Tex((string)t["file"]);
    if(n.Contains("albedo")||n.Contains("_diff")){m.SetTexture("_BaseMap",texture);m.SetColor("_BaseColor",Color.white);}
    if(n.Contains("normal")||n.Contains("nor_gl")){m.SetTexture("_BumpMap",texture);m.SetFloat("_BumpScale",.65f);m.EnableKeyword("_NORMALMAP");}
   }
   var packed=Tex(name+"_UnityMetalSmooth.png");
   if(packed){m.SetTexture("_MetallicGlossMap",packed);m.SetFloat("_Smoothness",.72f);m.EnableKeyword("_METALLICSPECGLOSSMAP");}
   if(name=="Weathered clean concrete plinth"){
    m.SetTexture("_BaseMap",Tex("PHConcrete_diff.png"));m.SetColor("_BaseColor",new Color(.72f,.76f,.80f).gamma);
    m.SetTexture("_BumpMap",Tex("PHConcrete_nor_gl.png"));m.SetFloat("_BumpScale",.75f);m.EnableKeyword("_NORMALMAP");
    m.SetTexture("_MetallicGlossMap",Tex("PHConcrete_UnityMetalSmooth.png"));m.SetFloat("_Smoothness",1);m.SetFloat("_Metallic",0);m.EnableKeyword("_METALLICSPECGLOSSMAP");
   }
   if(name.Contains("lens")){m.shader=Shader.Find("Parking/Lamp");m.SetColor("_LampEmission",new Color(.65f,.8f,1)*3);}
   string path=Root+"Materials/"+Safe(name)+".mat";Save(m,path);mats[name]=AssetDatabase.LoadAssetAtPath<Material>(path);
  }
 }
 static void Meshes(JArray data){
  meshes=new Dictionary<string,Mesh>();meshMats=new Dictionary<string,Material[]>();
  foreach(var d in data){
   string name=(string)d["name"];float[] v=d["vertices"].ToObject<float[]>(),n=d["normals"].ToObject<float[]>(),uv=d["uv"].ToObject<float[]>();
   var vertices=new Vector3[v.Length/3];var normals=new Vector3[vertices.Length];var uvs=new Vector2[vertices.Length];
   for(int i=0;i<vertices.Length;i++){vertices[i]=new Vector3(v[i*3],v[i*3+1],v[i*3+2]);normals[i]=new Vector3(n[i*3],n[i*3+1],n[i*3+2]);uvs[i]=new Vector2(uv[i*2],uv[i*2+1]);}
   // Decimation pushed a few remote plinth vertices below the source floor.
   if(name=="LampLOD")for(int i=0;i<vertices.Length;i++)vertices[i].y=Mathf.Max(0,vertices[i].y);
   var mesh=new Mesh{name=name,indexFormat=vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uvs;
   var subs=(JArray)d["submeshes"];mesh.subMeshCount=subs.Count;var materials=new Material[subs.Count];
   for(int i=0;i<subs.Count;i++){mesh.SetTriangles(subs[i]["indices"].ToObject<int[]>(),i);materials[i]=mats[(string)subs[i]["material"]];}
   for(int s=0;s<subs.Count;s++)if((string)subs[s]["material"]=="Weathered clean concrete plinth")foreach(int index in subs[s]["indices"].ToObject<int[]>()){
    var p=vertices[index];var nrm=normals[index];bool top=Mathf.Abs(nrm.y)>.6f;uvs[index]=(top?new Vector2(p.x,p.z):Mathf.Abs(nrm.x)>Mathf.Abs(nrm.z)?new Vector2(p.z,p.y):new Vector2(p.x,p.y))*.5f+new Vector2(.5f,top?.58f:.44f);
   }
   mesh.uv=uvs;mesh.RecalculateBounds();mesh.RecalculateTangents();string path=Root+"Meshes/"+name+".asset";Save(mesh,path);
   meshes[name]=AssetDatabase.LoadAssetAtPath<Mesh>(path);meshMats[name]=materials;
  }
 }
 static MeshRenderer Model(string meshName,Transform parent,string name=null,Vector3 position=default){
  var go=new GameObject(name??meshName);go.transform.SetParent(parent,false);go.transform.localPosition=position;
  go.AddComponent<MeshFilter>().sharedMesh=meshes[meshName];var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=meshMats[meshName];r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;r.lightProbeUsage=LightProbeUsage.BlendProbes;return r;
 }
 static UniversalRenderPipelineAsset Pipeline(){
  var rd=ScriptableObject.CreateInstance<UniversalRendererData>();rd.name="ParkingForward";rd.renderingMode=RenderingMode.ForwardPlus;
  var so=new SerializedObject(rd);so.FindProperty("postProcessData").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");so.ApplyModifiedPropertiesWithoutUndo();
  string rp=Root+"Settings/ParkingForward.asset";Save(rd,rp);rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rp);
  var urp=UniversalRenderPipelineAsset.Create(rd);urp.name="ParkingURP";urp.supportsHDR=true;urp.msaaSampleCount=4;urp.renderScale=1;urp.useSRPBatcher=true;
  urp.supportsCameraDepthTexture=false;urp.supportsCameraOpaqueTexture=false;
  so=new SerializedObject(urp);so.FindProperty("m_AdditionalLightsRenderingMode").intValue=1;so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=4;
  so.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=2048;so.FindProperty("m_MainLightShadowsSupported").boolValue=false;so.FindProperty("m_SoftShadowsSupported").boolValue=true;so.FindProperty("m_ShadowDistance").floatValue=12;so.ApplyModifiedPropertiesWithoutUndo();
  string pp=Root+"Settings/ParkingURP.asset";Save(urp,pp);return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pp);
 }
 [MenuItem("Spatial Wallpaper/Parking/Rebuild scene and player")]
 public static void Build(){
  foreach(var f in new[]{"Materials","Meshes","Scenes","Prefabs","Settings"})Directory.CreateDirectory(Root+f);
  Directory.CreateDirectory("../Validation/Parking");AssetDatabase.Refresh();Import();
  var data=JObject.Parse(File.ReadAllText(Gen+"scene.json"));Materials((JArray)data["materials"]);Meshes((JArray)data["meshes"]);
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var frame=new GameObject("DisplayWindow_XY_Z0");var model=new GameObject("SceneModel");model.transform.SetParent(frame.transform,false);
  string boxPath=Root+"Settings/ParkingBox.asset";
  var boxSettings=AssetDatabase.LoadAssetAtPath<ParkingBoxSettings>(boxPath);
  if(!boxSettings){boxSettings=ScriptableObject.CreateInstance<ParkingBoxSettings>();AssetDatabase.CreateAsset(boxSettings,boxPath);}
  float depth=Mathf.Clamp(boxSettings.depth,39,360);
  var box=model.AddComponent<DisplaySceneVolume>();box.size=new Vector3(.53f,.298125f,depth*Scale);
  var content=new GameObject("ParkingContent");content.transform.SetParent(model.transform,false);content.transform.localScale=Vector3.one*Scale;content.transform.localPosition=new Vector3(0,-3.375f*Scale,0);
  content.AddComponent<ParkingEnvironmentBinding>();
  var stack=new GameObject("TelevisionStack");stack.transform.SetParent(content.transform,false);
  foreach(var name in meshes.Keys.Where(x=>x.StartsWith("TV_")))Model(name,stack.transform);
  var main=Model("LampNear",content.transform,"MainStreetLamp",new Vector3(0,0,10));
  main.transform.localScale=Vector3.one*LampScale;
  var field=content.AddComponent<ParkingLampField>();field.mesh=meshes["LampLOD"];field.materials=meshMats["LampLOD"];
  field.instanceScale=LampScale;
  var positions=new List<Vector3>();var nearPositions=new List<Vector3>{new Vector3(0,0,10)};int near=0;
  // The box fixes the aperture and floor plane, not the lateral extent of an open environment.
  foreach(var l in data["lamps"]){var p=V(l["position"]);if(p==new Vector3(0,0,10))continue;
   if(p.z>depth-1||p.z<1||Mathf.Abs(p.x)>(p.z+14)*1.5f+24)continue;
   if(p.z<38&&Mathf.Abs(p.x)<25){var fixture=Model("LampNear",content.transform,"NearLamp_"+near++,p);fixture.transform.localScale=Vector3.one*LampScale;nearPositions.Add(p);}else positions.Add(p);
  }
  field.positions=positions.ToArray();
  float extent=depth*2+40;
  var floorMesh=new Mesh{name="ContinuousAsphalt"};var floorVertices=new List<Vector3>();var floorTriangles=new List<int>();
  foreach(float x in new[]{-extent,-6,6,extent}){floorVertices.Add(new Vector3(x,0,0));floorVertices.Add(new Vector3(x,0,depth));}
  for(int j=0;j<3;j++){int a=j*2;floorTriangles.AddRange(new[]{a,a+1,a+3,a,a+3,a+2});}
  floorMesh.SetVertices(floorVertices);floorMesh.SetTriangles(floorTriangles,0);floorMesh.RecalculateNormals();floorMesh.RecalculateBounds();Save(floorMesh,Root+"Meshes/Ground.asset");
  var floorGo=new GameObject("Asphalt");floorGo.transform.SetParent(content.transform,false);floorGo.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"Meshes/Ground.asset");var floor=floorGo.AddComponent<MeshRenderer>();
  var groundMat=Make("NightAsphalt","Parking/Asphalt");groundMat.SetTexture("_BaseMap",Tex("clean_asphalt_diff_4k_jpg.png"));groundMat.SetTexture("_MacroMap",Tex("aerial_asphalt_01_macro_blurred_4k_png.png"));groundMat.SetTexture("_NormalMap",Tex("clean_asphalt_nor_gl_4k_exr.png"));groundMat.SetTexture("_WearMap",Tex("Asphalt_Weathering_26m_RGB_png.png"));Save(groundMat,Root+"Materials/Asphalt.mat");floor.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Asphalt.mat");
  floor.sharedMaterial.SetTexture("_RoughMap",Tex("aerial_asphalt_01_rough_4k_png.png"));EditorUtility.SetDirty(floor.sharedMaterial);
  var atmosphere=content.AddComponent<ParkingAtmosphere>();atmosphere.ground=floor;atmosphere.mainFixture=main;atmosphere.televisionStack=stack;
  atmosphere.screens=new Renderer[2];atmosphere.screenLights=new Light[2];atmosphere.screenCentres=new Vector3[2];
  int k=0;foreach(var s in data["screens"]){
   var screen=stack.transform.Find((string)s["name"]).GetComponent<MeshRenderer>();var material=Make("CRT_"+k,"Parking/CRT");material.SetFloat("_Seed",17+k*14);Save(material,Root+"Materials/CRT_"+k+".mat");screen.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/CRT_"+k+".mat");
   atmosphere.screens[k]=screen;atmosphere.screenCentres[k]=V(s["center"]);
   var light=new GameObject("ScreenSpill_"+k).AddComponent<Light>();light.transform.SetParent(stack.transform,false);light.transform.localPosition=V(s["center"])+new Vector3(0,0,-.15f);light.type=LightType.Point;light.range=2.4f*Scale;light.intensity=.002f;light.color=new Color(.55f,.75f,1);light.shadows=LightShadows.None;atmosphere.screenLights[k++]=light;
  }
  var street=new GameObject("NearStreetLight").AddComponent<Light>();street.transform.SetParent(content.transform,false);street.transform.localPosition=new Vector3(0,7.2f,10);street.transform.localRotation=Quaternion.Euler(90,0,0);street.type=LightType.Spot;street.spotAngle=115;street.innerSpotAngle=70;street.range=18*Scale;street.intensity=.018f;street.color=new Color(.67f,.8f,1);street.shadows=LightShadows.None;atmosphere.streetLight=street;
  street.shadows=LightShadows.Soft;street.shadowBias=.015f;street.shadowNormalBias=.02f;street.shadowNearPlane=.005f;
  street.transform.localPosition=new Vector3(0,7.2f*LampScale,10);
  var fieldLights=new List<Light>();
  foreach(var p in nearPositions){if(p==new Vector3(0,0,10))continue;
   var l=new GameObject("LocalStreetSpill").AddComponent<Light>();l.transform.SetParent(content.transform,false);l.transform.localPosition=p+new Vector3(0,7.2f,0);l.transform.localRotation=Quaternion.Euler(90,0,0);l.type=LightType.Spot;l.spotAngle=125;l.innerSpotAngle=90;l.color=new Color(.78f,.84f,.94f);l.intensity=.018f;l.range=20*Scale;
   l.transform.localPosition=p+new Vector3(0,7.2f*LampScale,0);l.shadows=p.z<18&&Mathf.Abs(p.x)<=12?LightShadows.Soft:LightShadows.None;l.shadowBias=.015f;l.shadowNormalBias=.02f;l.shadowNearPlane=.005f;fieldLights.Add(l);
  }
  atmosphere.fieldLights=fieldLights.ToArray();street.color=new Color(.78f,.84f,.94f);street.spotAngle=125;street.innerSpotAngle=90;
  foreach(var l in fieldLights){l.spotAngle=100;l.innerSpotAngle=65;}street.spotAngle=100;street.innerSpotAngle=65;
  var edge=new GameObject("ForegroundOffscreenSpill").AddComponent<Light>();edge.transform.SetParent(content.transform,false);edge.transform.localPosition=new Vector3(-5,6,-2);edge.transform.localRotation=Quaternion.Euler(90,0,0);edge.type=LightType.Spot;edge.spotAngle=145;edge.innerSpotAngle=110;edge.color=new Color(.78f,.84f,.94f);edge.shadows=LightShadows.None;atmosphere.edgeLight=edge;
  edge.transform.localPosition=new Vector3(-5,6,.1f);
  var key=new GameObject("MoonFill").AddComponent<Light>();key.transform.SetParent(content.transform,false);key.type=LightType.Directional;key.transform.localRotation=Quaternion.Euler(50,-35,0);key.intensity=.35f;key.color=new Color(.76f,.82f,.90f);key.shadows=LightShadows.None;RenderSettings.sun=key;
  RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.11f,.12f,.14f);RenderSettings.fog=false;
  var profile=ScriptableObject.CreateInstance<VolumeProfile>();var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(1);bloom.intensity.Override(.12f);bloom.scatter.Override(.5f);
  var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);var ca=profile.Add<ColorAdjustments>(true);ca.postExposure.Override(.8f);ca.saturation.Override(-18);
  var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.17f);vignette.smoothness.Override(.6f);
  string vp=Root+"Settings/Night.asset";AssetDatabase.DeleteAsset(vp);AssetDatabase.CreateAsset(profile,vp);foreach(var c in profile.components)AssetDatabase.AddObjectToAsset(c,profile);
  var volume=new GameObject("NightGrade").AddComponent<Volume>();volume.transform.SetParent(content.transform,false);volume.isGlobal=true;volume.sharedProfile=profile;
  PrefabUtility.SaveAsPrefabAsset(content,Root+"Prefabs/ParkingContent.prefab");
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.001f,.002f,.004f);camera.allowHDR=true;
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.contentDepth=box.size.z*4+.1f;projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;
  var preview=camera.gameObject.AddComponent<PreviewEyeDriver>();preview.projection=projection;preview.cadence=cadence;preview.exhibit=stack;
  camera.gameObject.AddComponent<ParkingControls>().atmosphere=atmosphere;
  var benchmark=camera.gameObject.AddComponent<ShowcaseBenchmark>();benchmark.projection=projection;benchmark.cadence=cadence;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=model.transform;
  var binding=camera.gameObject.AddComponent<ParkingRenderBinding>();binding.content=atmosphere;binding.cadence=cadence;
  var pipeline=camera.gameObject.AddComponent<ParkingPipelineBinding>();pipeline.enabled=false;pipeline.pipeline=Pipeline();pipeline.useRestoreOverride=true;pipeline.restoreOverride=GraphicsSettings.defaultRenderPipeline;pipeline.enabled=true;
  camera.gameObject.AddComponent<ParkingCapture>();
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Root+"Scenes/Parking.unity");AssetDatabase.SaveAssets();
  File.WriteAllText("../Validation/Parking/checks.txt",ParkingChecks.Run()+$"\nScene: 8 TVs, 2 animated screens, {near+1} near fixtures, {positions.Count} instanced fixtures; {nearPositions.Count+3} local lights including 3 soft-shadow spots.\n");
  BuildPlayerOnly();
 }
 public static void BuildPlayerOnly(){
  EditorSceneManager.OpenScene(Root+"Scenes/Parking.unity");
  foreach(string shader in new[]{"Parking/Asphalt","Parking/CRT","Parking/Lamp"})if(ShaderUtil.ShaderHasError(Shader.Find(shader)))throw new Exception("Shader compilation failed: "+shader);
  Directory.CreateDirectory("Builds/Parking");
  var previous=QualitySettings.renderPipeline;
  UnityEditor.Build.Reporting.BuildReport report;
  try{QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root+"Settings/ParkingURP.asset");
   report=BuildPipeline.BuildPlayer(new[]{Root+"Scenes/Parking.unity"},"Builds/Parking/Parking.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  }finally{QualitySettings.renderPipeline=previous;AssetDatabase.SaveAssets();}
  File.WriteAllText("../Validation/Parking/build.txt",report.summary.result+" bytes="+report.summary.totalSize+" errors="+report.summary.totalErrors);
  if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Parking build failed");
 }
}

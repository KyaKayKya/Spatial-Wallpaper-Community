using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using SpatialWallpaper.Train;
public static class TrainBuilder {
 const string Root="Assets/Train/";
 const float W=4.266666667f,H=2.4f,D=6,Scale=.53f/W;
 public static void Inspect(){
  EditorSceneManager.OpenScene(Root+"Scenes/Train.unity");var text=new System.Text.StringBuilder();
  foreach(var f in UnityEngine.Object.FindObjectsOfType<MeshFilter>()){
   var min=Vector3.one*float.PositiveInfinity;var max=Vector3.one*float.NegativeInfinity;
   foreach(var v in f.sharedMesh.vertices){var p=f.transform.TransformPoint(v);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
   text.AppendLine(f.name+" world="+min.ToString("F5")+" .. "+max.ToString("F5")+" local="+f.sharedMesh.bounds+" matrix="+f.transform.localToWorldMatrix);
  }
  File.WriteAllText("../Validation/Train/imported-bounds.txt",text.ToString());
 }
 static Material Material(string name,string shader){string path=Root+"Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}else m.shader=Shader.Find(shader);return m;}
 static void ValidateImported(TrainJourney content,DisplaySceneVolume box){
  bool left=false,right=false;int count=0;
  foreach(var f in content.GetComponentsInChildren<MeshFilter>())foreach(var v in f.sharedMesh.vertices){
   var p=f.transform.TransformPoint(v);count++;
   if(!box.ContainsWorld(p,.00002f))throw new Exception("Train imported vertex outside box: "+f.name+" "+p);
   left|=Vector3.Distance(p,box.FrontCorner(-.5f,-.5f))<.00002f;right|=Vector3.Distance(p,box.FrontCorner(.5f,-.5f))<.00002f;
  }
  if(!left||!right)throw new Exception("Train imported floor does not meet display corners");
  File.WriteAllText("../Validation/Train/import-checks.txt",$"PASS: {count} actual imported vertices; floor lower corners match; all geometry in fixed volume.\n");
 }
 public static void Build(){
  foreach(var dir in new[]{"Materials","Scenes","Prefabs"})Directory.CreateDirectory(Root+dir);
  Directory.CreateDirectory("../Validation/Train");AssetDatabase.Refresh();
  var manifest=JObject.Parse(File.ReadAllText(Root+"Generated/manifest.json"));
  if(manifest["sha256"]==null||manifest["sha256"].Count()!=9)throw new Exception("Train export manifest is incomplete");
  foreach(var item in manifest["sha256"].Children<JProperty>()){
   using(var sha=SHA256.Create())using(var file=File.OpenRead(Root+"Generated/"+item.Name)){
    string hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();if(hash!=(string)item.Value)throw new Exception("Train mixed/stale export: "+item.Name);
   }
  }
  foreach(string name in new[]{"Architecture","Furnishings","Details","Lamps"})foreach(string ext in new[]{".png",".fbx"})if(!File.Exists(Root+"Generated/"+name+ext))throw new Exception("Train bake/export not ready: "+name+ext);
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var frame=new GameObject("DisplayWindow_XY_Z0");var model=new GameObject("SceneModel");model.transform.SetParent(frame.transform,false);
  var box=model.AddComponent<DisplaySceneVolume>();box.size=new Vector3(.53f,.298125f,D*Scale);box.showGuide=false;
  var content=new GameObject("TrainContent");content.transform.SetParent(model.transform,false);content.transform.localScale=Vector3.one*Scale;
  var journey=content.AddComponent<TrainJourney>();
  foreach(string name in new[]{"Architecture","Furnishings","Details","Lamps"}){
   string texturePath=Root+"Generated/"+name+".png";
   var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);importer.sRGBTexture=true;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.mipmapEnabled=true;importer.anisoLevel=8;importer.SaveAndReimport();
   string modelPath=Root+"Generated/"+name+".fbx";var modelImporter=(ModelImporter)AssetImporter.GetAtPath(modelPath);modelImporter.isReadable=true;modelImporter.SaveAndReimport();
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);if(!source)throw new Exception("Missing Train export "+name);
   var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.name=name;go.transform.SetParent(content.transform,false);go.transform.localPosition=new Vector3(0,-H/2,0);
   // Blender FBX imports source +Y as Unity -Z (and mirrors X); rotate the asset, never the display camera.
   go.transform.localRotation=Quaternion.Euler(0,180,0)*go.transform.localRotation;
   var mat=Material(name,"Universal Render Pipeline/Unlit");mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));mat.SetColor("_BaseColor",Color.white);
   foreach(var r in go.GetComponentsInChildren<MeshRenderer>()){r.sharedMaterials=Enumerable.Repeat(mat,r.sharedMaterials.Length).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;}
   if(name=="Furnishings")journey.furnishings=go.transform;
  }
  journey.windows=new Renderer[6];
  var treeImport=(TextureImporter)AssetImporter.GetAtPath(Root+"Textures/SnowTrees.png");treeImport.sRGBTexture=true;treeImport.alphaIsTransparency=true;treeImport.wrapMode=TextureWrapMode.Clamp;treeImport.maxTextureSize=2048;treeImport.mipmapEnabled=true;treeImport.textureCompression=TextureImporterCompression.CompressedHQ;treeImport.SaveAndReimport();
  var panoImport=(TextureImporter)AssetImporter.GetAtPath(Root+"Generated/SnowyPark.png");panoImport.sRGBTexture=true;panoImport.wrapModeU=TextureWrapMode.Repeat;panoImport.wrapModeV=TextureWrapMode.Clamp;panoImport.maxTextureSize=4096;panoImport.mipmapEnabled=true;panoImport.SaveAndReimport();
  for(int i=0;i<6;i++){
   var go=new GameObject("WindowLayer_"+i);go.transform.SetParent(content.transform,false);
   float z=new[]{5.8f,5.45f,5.05f,4.7f,4.5f,4.242f}[i];
   var mesh=new Mesh{name="FiniteWindow_"+i};float x=i==5?1.035f:W/2,y0=i==5?.755f:0,y1=i==5?1.965f:H;
   mesh.vertices=new[]{new Vector3(-x,y0-H/2,z),new Vector3(x,y0-H/2,z),new Vector3(x,y1-H/2,z),new Vector3(-x,y1-H/2,z)};
   mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateNormals();mesh.RecalculateBounds();string meshPath=Root+"Materials/WindowMesh"+i+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,meshPath);
   go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();var mat=Material("Window"+i,"Train/WinterJourney");mat.SetFloat("_Layer",i);mat.SetTexture("_Panorama",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Generated/SnowyPark.png"));mat.SetTexture("_Trees",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/SnowTrees.png"));mat.renderQueue=3000+i;r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;journey.windows[i]=r;
  }
  ValidateImported(journey,box);
  TrainGeometryValidation.Validate(journey,box,Scale);
  PrefabUtility.SaveAsPrefabAsset(content,Root+"Prefabs/TrainContent.prefab");
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.025f,.018f);camera.allowHDR=false;
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.contentDepth=D*Scale*4+.1f;projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;
  var driver=camera.gameObject.AddComponent<PreviewEyeDriver>();driver.projection=projection;driver.cadence=cadence;driver.exhibit=content;
  var bench=camera.gameObject.AddComponent<ShowcaseBenchmark>();bench.projection=projection;bench.cadence=cadence;
  var binding=camera.gameObject.AddComponent<TrainRenderBinding>();binding.content=journey;binding.cadence=cadence;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=model.transform;
  camera.gameObject.AddComponent<TrainCapture>();
  var light=new GameObject("BakedLightingReference").AddComponent<Light>();light.type=LightType.Directional;light.intensity=0;
  RenderSettings.skybox=null;RenderSettings.fog=false;
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Root+"Scenes/Train.unity");AssetDatabase.SaveAssets();
  var report=BuildPipeline.BuildPlayer(new[]{Root+"Scenes/Train.unity"},"Builds/Train/Train.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Train build failed");
  if(ShaderUtil.ShaderHasError(Shader.Find("Train/WinterJourney")))throw new Exception("Train window shader compilation failed");
 }
}

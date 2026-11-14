using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class MiniShotBuilder {
 const string Root="Assets/MiniShotGame/";
 const string Scene=Root+"Scenes/MiniShotGame.unity";

 [MenuItem("Spatial Wallpaper/MiniShotGame/Open scene")]
 public static void Open(){EditorSceneManager.OpenScene(Scene);}

 [MenuItem("Spatial Wallpaper/MiniShotGame/Rebuild scene and player")]
 public static void Build(){
  foreach(string dir in new[]{"Meshes","Materials","Scenes","Prefabs"})Directory.CreateDirectory(Root+dir);
  AssetDatabase.Refresh();
  var manifest=JObject.Parse(File.ReadAllText(Root+"Generated/manifest.json"));
  if((int)manifest["format"]!=1||manifest["objects"].Count()!=28)throw new Exception("MiniShotGame export manifest is incomplete");
  // Verify the whole export before replacing any scene or generated Mesh assets.
  foreach(var item in manifest["objects"]){
   string file=(string)item["file"];
   if(Path.GetFileName(file)!=file||!file.EndsWith(".bytes",StringComparison.Ordinal))throw new Exception("Invalid export filename");
   byte[] bytes=File.ReadAllBytes(Root+"Generated/"+file);
   using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=(string)item["sha256"])throw new Exception("MiniShotGame stale/corrupt export: "+file);
  }
  var shader=Shader.Find("MiniShotGame/VertexEmission");
  if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("MiniShotGame vertex emission shader is unavailable");
  var gridShader=Shader.Find("MiniShotGame/FilteredGrid");
  if(!gridShader||ShaderUtil.ShaderHasError(gridShader))throw new Exception("MiniShotGame filtered grid shader is unavailable");
  string matPath=Root+"Materials/VertexEmission.mat";
  var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
  if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,matPath);}else material.shader=shader;
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var frame=new GameObject("DisplayWindow_XY_Z0");
  // Bind the scene's existing lightweight pipeline independently of the
  // project's default (other scenes may select their own pipeline).
  frame.AddComponent<SceneRenderSettings>().pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Showcase/Settings/ShowcaseURP.asset");
  var model=new GameObject("SceneModel");model.transform.SetParent(frame.transform,false);
  var box=model.AddComponent<DisplaySceneVolume>();box.size=new Vector3(.53f,.298125f,3.6f*.53f/1.6f);box.showGuide=false;
  var content=new GameObject("MiniShotContent");content.transform.SetParent(model.transform,false);content.transform.localScale=Vector3.one*(.53f/1.6f);
  foreach(var item in manifest["objects"]){
   string name=(string)item["name"];
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
   using(var reader=new BinaryReader(File.OpenRead(Root+"Generated/"+(string)item["file"]))){
    if(reader.ReadUInt32()!=0x4D534731)throw new Exception("MiniShotGame mesh format mismatch");
    int count=reader.ReadInt32(),indexCount=reader.ReadInt32();
    if(count!=(int)item["vertices"]||indexCount!=(int)item["indices"]||reader.BaseStream.Length!=12L+28L*count+4L*indexCount)throw new Exception("MiniShotGame mesh length mismatch");
    var vertices=new Vector3[count];var colors=new Color[count];var indices=new int[indexCount];
    for(int i=0;i<count;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());colors[i]=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());}
    for(int i=0;i<indexCount;i++){indices[i]=reader.ReadInt32();if(indices[i]<0||indices[i]>=count)throw new Exception("MiniShotGame mesh index out of range");}
    mesh.vertices=vertices;mesh.colors=colors;mesh.triangles=indices;mesh.RecalculateBounds();
   }
   string path=Root+"Meshes/"+name+".asset";
   var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,path);
   var obj=new GameObject(name);obj.transform.SetParent(content.transform,false);
   obj.AddComponent<MeshFilter>().sharedMesh=mesh;
   var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   if(name.StartsWith("Grid_",StringComparison.Ordinal)||name=="Back_grid_fully_in_black")renderer.enabled=false;
   if(name=="Floor"||name=="Ceiling"||name=="Wall_left"||name=="Wall_right"){
    bool vertical=name.StartsWith("Wall_",StringComparison.Ordinal);
    string gridPath=Root+"Materials/"+(vertical?"WallGrid":"FloorGrid")+".mat";
    var gridMaterial=AssetDatabase.LoadAssetAtPath<Material>(gridPath);
    if(!gridMaterial){gridMaterial=new Material(gridShader);AssetDatabase.CreateAsset(gridMaterial,gridPath);}
    gridMaterial.SetFloat("_Vertical",vertical?1:0);gridMaterial.SetFloat("_Span",vertical?.9f:1.6f);gridMaterial.SetFloat("_Divisions",vertical?8:12);
    renderer.sharedMaterial=gridMaterial;
   }
  }
  PrefabUtility.SaveAsPrefabAsset(content,Root+"Prefabs/MiniShotContent.prefab");
  var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.tag="MainCamera";
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;projection.contentDepth=3;
  projection.SetEye(projection.referenceEye);
  var cadence=camera.gameObject.AddComponent<AdaptiveRenderCadence>();cadence.projection=projection;
  var preview=camera.gameObject.AddComponent<PreviewEyeDriver>();preview.projection=projection;preview.cadence=cadence;preview.exhibit=content;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=model.transform;
  var capture=camera.gameObject.AddComponent<MiniShotCapture>();capture.content=content;capture.volume=box;
  var light=new GameObject("UnlitReferenceLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=0;
  RenderSettings.skybox=null;RenderSettings.fog=false;
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);AssetDatabase.SaveAssets();
  MiniShotChecks.Validate();
  var result=BuildPipeline.BuildPlayer(new[]{Scene},"Builds/MiniShotGame/MiniShotGame.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("MiniShotGame player build failed");
  Debug.Log("MINISHOT_BUILD_OK");
 }
}

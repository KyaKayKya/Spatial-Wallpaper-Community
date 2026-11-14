using System;using System.IO;using System.Linq;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;

public static class MiniShotSdkBuilder {
 public const string Folder="Assets/MiniShotGameSdk/Generated";
 public const string ScenePath=Folder+"/MiniShotGameSdk.unity";
 static T Save<T>(T value,string name)where T:UnityEngine.Object {
  string path=Folder+"/"+name;var previous=AssetDatabase.LoadAssetAtPath<T>(path);
  if(previous){EditorUtility.CopySerialized(value,previous);UnityEngine.Object.DestroyImmediate(value);return previous;}
  AssetDatabase.CreateAsset(value,path);return value;
 }
 static Material Material(string shader,string name){var source=Shader.Find(shader);if(!source)throw new InvalidOperationException("MiniShot SDK shader missing: "+shader);return Save(new Material(source),name+".mat");}
 static AudioClip Clip(string name){string path="Assets/MiniShotGameSdk/Audio/"+name+".wav";AssetDatabase.ImportAsset(path);var importer=AssetImporter.GetAtPath(path) as AudioImporter;if(!importer)throw new InvalidDataException("MiniShot SDK audio missing: "+path);var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;importer.defaultSampleSettings=settings;importer.loadInBackground=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<AudioClip>(path);}
 public static SceneDefinition Create(){
  Directory.CreateDirectory(Folder);AssetDatabase.Refresh();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);PlayerSettings.colorSpace=ColorSpace.Linear;
  if(!GraphicsSettings.currentRenderPipeline)TrustedRenderProfileBuilder.Install(Folder);
  var manifest=JObject.Parse(File.ReadAllText("Assets/MiniShotGameSdk/Source/manifest.json"));
  if((int)manifest["format"]!=1||manifest["objects"].Count()!=28||manifest["targets"].Count()!=10)throw new InvalidDataException("MiniShot source inventory");
  float scale=(float)manifest["scale"];
  var root=new GameObject("MiniShotGame SDK");var definition=root.AddComponent<SceneDefinition>();definition.templateId="fb4e7430-971a-45cf-888a-ae550cc0158b";definition.displayName="先行者 · SDK";definition.authorFrame=new Vector3(.53f,.298125f,1.2f);definition.contentRoot=root.transform;definition.reshare=false;
  definition.licenseName="User supplied MiniShotGame — internal use only";definition.licenseSource="Local spatial_targets_v2_package.zip; public redistribution unverified";definition.licenseAttribution="Original supplied MiniShotGame geometry; Spatial Wallpaper SDK adaptation";
  var settings=root.AddComponent<ScenePresentationSettings>();settings.customBackground=true;settings.backgroundColor=Color.black;settings.applyInPlay=true;settings.postProcessing=false;
  var content=new GameObject("Original target room");content.transform.SetParent(root.transform,false);content.transform.localScale=Vector3.one*scale;
  var author=root.AddComponent<MiniShotSandbox>();author.centers=new Vector3[10];author.radii=new float[10];author.targets=new Material[10];author.keys=new string[10];author.hits=new bool[10];author.dirty=new bool[10];author.shown=-1;
  author.music=Clip("Music");author.hitSound=Clip("Hit");author.resetSound=Clip("Reset");
  var targets=((JArray)manifest["targets"]).Cast<JObject>().OrderBy(t=>(string)t["name"],StringComparer.Ordinal).ToArray();
  for(int i=0;i<targets.Length;i++){var c=targets[i]["center"];author.centers[i]=new Vector3((float)c[0],(float)c[1],(float)c[2])*scale;author.radii[i]=(float)targets[i]["radius"]*scale;author.keys[i]="minishot.v1.target-"+(i+1).ToString("D2");}
  var surface=Material("MiniShotGame/VertexEmission","OriginalEmission");var floor=Material("MiniShotGame/FilteredGrid","FloorGrid");floor.SetFloat("_Vertical",0);floor.SetFloat("_Span",1.6f);floor.SetFloat("_Divisions",12);
  var wall=Material("MiniShotGame/FilteredGrid","WallGrid");wall.SetFloat("_Vertical",1);wall.SetFloat("_Span",.9f);wall.SetFloat("_Divisions",8);
  foreach(JObject item in manifest["objects"]){
   string name=(string)item["name"],file=(string)item["file"];
   if(Path.GetFileName(file)!=file)throw new InvalidDataException("MiniShot source file");byte[] bytes=File.ReadAllBytes("Assets/MiniShotGameSdk/Source/"+file);
   using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=(string)item["sha256"])throw new InvalidDataException("MiniShot source digest: "+file);
   if(name.StartsWith("Grid_",StringComparison.Ordinal)||name=="Back_grid_fully_in_black")continue;
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
   using(var reader=new BinaryReader(new MemoryStream(bytes))){
    if(reader.ReadUInt32()!=0x4D534731)throw new InvalidDataException("MiniShot source mesh format");int count=reader.ReadInt32(),indices=reader.ReadInt32();
    if(count!=(int)item["vertices"]||indices!=(int)item["indices"]||bytes.Length!=12L+28L*count+4L*indices)throw new InvalidDataException("MiniShot source mesh size");
    var vertices=new Vector3[count];var colors=new Color[count];var triangles=new int[indices];bool room=name=="Floor"||name=="Ceiling"||name.StartsWith("Wall_",StringComparison.Ordinal);
    for(int i=0;i<count;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(room)vertices[i].x*=2;colors[i]=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());}
    for(int i=0;i<indices;i++){triangles[i]=reader.ReadInt32();if(triangles[i]<0||triangles[i]>=count)throw new InvalidDataException("MiniShot source mesh index");}
    mesh.vertices=vertices;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
   }
   mesh=Save(mesh,name+".asset");var go=new GameObject(name);go.transform.SetParent(content.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   int target=Array.FindIndex(targets,t=>(string)t["name"]==name);
   if(target>=0){var mat=Material("MiniShotGameSdk/Target","Target"+target);mat.SetFloat("_Hit",0);renderer.sharedMaterial=mat;author.targets[target]=mat;}
   else renderer.sharedMaterial=name=="Floor"||name=="Ceiling"?floor:name=="Wall_left"||name=="Wall_right"?wall:surface;
  }
  var indicator=GameObject.CreatePrimitive(PrimitiveType.Cube);indicator.name="Save status";UnityEngine.Object.DestroyImmediate(indicator.GetComponent<Collider>());indicator.transform.SetParent(root.transform,false);indicator.transform.localPosition=new Vector3(.245f,.13f,.02f);indicator.transform.localScale=new Vector3(.005f,.005f,.001f);author.status=Material("Universal Render Pipeline/Unlit","StorageStatus");author.status.SetColor("_BaseColor",new Color(.85f,.4f,.04f,1));indicator.GetComponent<MeshRenderer>().sharedMaterial=author.status;
  author.indicator=indicator.transform;
  var camera=new GameObject("Author fixed window").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.displayWidth=.53f;projection.displayHeight=.298125f;projection.contentDepth=1.2f;projection.SetEye(new Vector3(0,0,-.6f));RenderSettings.skybox=null;
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();return definition;
 }
 [MenuItem("Spatial Wallpaper/MiniShotGame SDK/Create author scene")]
 public static void Menu(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())Create();}
 public static void Build(){string output=Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT");try{
  var scene=Create();var author=scene.GetComponent<MiniShotSandbox>();
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"scene",ScenePath},{"targets",author.targets.Length},{"foreground_targets",author.centers.Count(p=>p.z<0)},{"visible_original_meshes",25},{"source_sha256",JObject.Parse(File.ReadAllText("Assets/MiniShotGameSdk/Source/manifest.json"))["sourceSha256"]},{"room_width_multiplier",2},{"scope","SDK author scene creation; sandbox preview/export still required"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
}

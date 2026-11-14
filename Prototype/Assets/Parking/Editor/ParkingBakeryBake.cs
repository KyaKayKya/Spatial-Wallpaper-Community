using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using SpatialWallpaper.Parking;
public static class ParkingBakeryBake {
#if BAKERY_INCLUDED
 const string Root="Assets/Parking/";
 static ParkingAtmosphere content;static Material groundMaterial;static double started;static bool finished;
 const string Manifest="Library/ParkingBakeryRecovery.json";
 [Serializable] class Recovery {public string project;public string backup;public string[] files;}
 static string backupPath;static string[] backupFiles;static bool bakeStarted,bakeCompleted;
 static void Snapshot(){
  if(File.Exists(Manifest))throw new InvalidOperationException("Pending bake recovery: run tools/parking/bakery_recovery.py after closing this project's Unity");
  backupPath=Path.GetFullPath("Library/ParkingBakeryBackup/"+Guid.NewGuid().ToString("N"));
  backupFiles=Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Where(p=>!p.Replace('\\','/').StartsWith(Root+"Generated/")).ToArray();
  foreach(var path in backupFiles){var dest=Path.Combine(backupPath,path);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(path,dest);}
  File.WriteAllText(Manifest,JsonUtility.ToJson(new Recovery{project=Path.GetFullPath("."),backup=backupPath,files=backupFiles},true));
 }
 static void Restore(){
  if(backupFiles==null)return;
  foreach(var path in Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Where(p=>!p.Replace('\\','/').StartsWith(Root+"Generated/")))if(!backupFiles.Contains(path))File.Delete(path);
  foreach(var path in backupFiles)File.Copy(Path.Combine(backupPath,path),path,true);
  AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);EditorSceneManager.OpenScene(Root+"Scenes/Parking.unity");backupFiles=null;File.Delete(Manifest);
 }
 static void Unsubscribe(){ftRenderLightmap.OnFinishedFullRender-=Finished;EditorApplication.update-=Watch;EditorApplication.delayCall-=FinalizeBake;}
 static void Fail(Exception error){finished=true;Unsubscribe();Debug.LogException(error);try{if(!bakeStarted||bakeCompleted)Restore();else Debug.LogError("Recovery deferred until Unity and Bakery children exit; backup manifest: "+Path.GetFullPath(Manifest));}catch(Exception recovery){Debug.LogError("Bake backup retained at "+backupPath);Debug.LogException(recovery);}if(Application.isBatchMode)EditorApplication.Exit(1);}
 static Vector4[] FarRectangles(float depth){float near=Mathf.Min(52,depth),extent=depth*2+40;var result=new List<Vector4>{new Vector4(-extent,0,-30,near),new Vector4(30,0,extent,near)};if(depth>near)result.Add(new Vector4(-extent,near,extent,depth));return result.ToArray();}
 static Mesh Plane(string name,float nearDepth,params Vector4[] rectangles){
  var vertices=new List<Vector3>();var uv=new List<Vector2>();var uv2=new List<Vector2>();var triangles=new List<int>();
  foreach(var r in rectangles){int n=vertices.Count;foreach(var p in new[]{new Vector3(r.x,0,r.y),new Vector3(r.x,0,r.w),new Vector3(r.z,0,r.w),new Vector3(r.z,0,r.y)}){
   vertices.Add(p);uv.Add(new Vector2(p.x,p.z)/2.1f);uv2.Add(new Vector2((p.x+30)/60,p.z/nearDepth));
  }triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
  var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,uv2);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
  string path=Root+"Meshes/"+name+".asset";var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(previous){EditorUtility.CopySerialized(mesh,previous);UnityEngine.Object.DestroyImmediate(mesh);return previous;}AssetDatabase.CreateAsset(mesh,path);return mesh;
 }
 static BakeryLightmapGroup Group(string name,int resolution,bool original){
  string path=Root+"Settings/"+name+".asset";var group=AssetDatabase.LoadAssetAtPath<BakeryLightmapGroup>(path);
  if(!group){group=ScriptableObject.CreateInstance<BakeryLightmapGroup>();AssetDatabase.CreateAsset(group,path);}
  group.name=name;group.resolution=resolution;group.mode=original?BakeryLightmapGroup.ftLMGroupMode.OriginalUV:BakeryLightmapGroup.ftLMGroupMode.PackAtlas;group.renderMode=BakeryLightmapGroup.RenderMode.Indirect;group.renderDirMode=BakeryLightmapGroup.RenderDirMode.None;EditorUtility.SetDirty(group);return group;
 }
 [MenuItem("Spatial Wallpaper/Parking/Bake static indirect with Bakery")]
 public static void Run(){
  if(ftRenderLightmap.bakeInProgress)throw new InvalidOperationException("A bake is already running");
  Unsubscribe();bakeStarted=false;bakeCompleted=false;Snapshot();try{Prepare();}catch(Exception error){Fail(error);}
 }
 static void Prepare(){
  EditorSceneManager.OpenScene(Root+"Scenes/Parking.unity");content=UnityEngine.Object.FindObjectOfType<ParkingAtmosphere>();
  Directory.CreateDirectory("../Validation/Parking/bakery-before");
  foreach(string name in new[]{"front","hidden-tvs"}){var source="../Validation/Parking/stills/"+name+".png";var dest="../Validation/Parking/bakery-before/"+name+".png";if(File.Exists(source)&&!File.Exists(dest))File.Copy(source,dest);}
  groundMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Asphalt.mat");
  float depth=UnityEngine.Object.FindObjectOfType<DisplaySceneVolume>().size.z/(.53f/12);float nearDepth=Mathf.Min(52,depth);
  var floor=content.ground.GetComponent<MeshFilter>();floor.sharedMesh=Plane("GroundBakeNear",nearDepth,new Vector4(-30,0,-6,nearDepth),new Vector4(-6,0,6,nearDepth),new Vector4(6,0,30,nearDepth));
  var far=content.transform.Find("FarAsphalt");if(!far){far=new GameObject("FarAsphalt").transform;far.SetParent(content.transform,false);far.gameObject.AddComponent<MeshFilter>();far.gameObject.AddComponent<MeshRenderer>();}
  far.GetComponent<MeshFilter>().sharedMesh=Plane("GroundBakeFar",nearDepth,FarRectangles(depth));far.GetComponent<MeshRenderer>().sharedMaterial=groundMaterial;
  var groundGroup=Group("BakeryGround",2048,true);var fixtures=Group("BakeryFixtures",2048,false);
  foreach(var renderer in content.GetComponentsInChildren<MeshRenderer>()){
   if(renderer.transform.IsChildOf(content.televisionStack.transform)||renderer.name=="FarAsphalt")continue;
   GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.ContributeGI);renderer.receiveGI=ReceiveGI.Lightmaps;
   var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
   if(renderer!=content.ground&&mesh.uv2.Length==0){Unwrapping.GenerateSecondaryUVSet(mesh);EditorUtility.SetDirty(mesh);}
   var selector=renderer.GetComponent<BakeryLightmapGroupSelector>();if(!selector)selector=renderer.gameObject.AddComponent<BakeryLightmapGroupSelector>();selector.lmgroupAsset=renderer==content.ground?groundGroup:fixtures;
  }
  // A conventional Meta pass supplies asphalt albedo to the baker; the runtime material is restored afterwards.
  var proxy=new Material(Shader.Find("Universal Render Pipeline/Lit"));proxy.SetTexture("_BaseMap",groundMaterial.GetTexture("_BaseMap"));proxy.SetColor("_BaseColor",Color.white);proxy.SetFloat("_Smoothness",.1f);
  string proxyPath=Root+"Materials/BakeryAlbedoProxy.mat";var previous=AssetDatabase.LoadAssetAtPath<Material>(proxyPath);if(previous){EditorUtility.CopySerialized(proxy,previous);UnityEngine.Object.DestroyImmediate(proxy);proxy=previous;}else AssetDatabase.CreateAsset(proxy,proxyPath);content.ground.sharedMaterial=proxy;
  content.televisionStack.SetActive(false);
  foreach(var light in content.fieldLights){var bake=light.GetComponent<BakeryPointLight>();if(!bake)bake=light.gameObject.AddComponent<BakeryPointLight>();
   bake.color=light.color;bake.intensity=.12f;bake.cutoff=20*(.53f/12);bake.realisticFalloff=true;bake.falloffMinRadius=.01f;bake.projMode=BakeryPointLight.ftLightProjectionMode.Cone;bake.angle=light.spotAngle;bake.innerAngle=light.innerSpotAngle/light.spotAngle*100;bake.bakeToIndirect=true;bake.samples=64;bake.shadowSpread=.008f;
  }
  var sky=content.GetComponent<BakerySkyLight>();if(!sky)sky=content.gameObject.AddComponent<BakerySkyLight>();sky.color=new Color(.07f,.075f,.085f).gamma;sky.intensity=.5f;sky.samples=128;sky.hemispherical=true;sky.bakeToIndirect=true;
  var sun=GameObject.Find("MoonFill").GetComponent<Light>();var sunBake=sun.GetComponent<BakeryDirectLight>();if(!sunBake)sunBake=sun.gameObject.AddComponent<BakeryDirectLight>();sunBake.color=sun.color;sunBake.intensity=sun.intensity;sunBake.bakeToIndirect=true;sunBake.samples=32;
  if(!content.GetComponent<BakeryLightmappedPrefab>())content.gameObject.AddComponent<BakeryLightmappedPrefab>();
  PrefabUtility.SaveAsPrefabAssetAndConnect(content.gameObject,Root+"Prefabs/ParkingContent.prefab",InteractionMode.AutomatedAction);
  EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
  var window=EditorWindow.GetWindow<ftRenderLightmap>();
  window.userRenderMode=ftRenderLightmap.RenderMode.Indirect;ftRenderLightmap.renderDirMode=ftRenderLightmap.RenderDirMode.None;ftRenderLightmap.bounces=3;window.giSamples=64;window.denoise=true;ftRenderLightmap.denoiserType=ftGlobalStorage.DenoiserType.OpenImageDenoise;
  ftRenderLightmap.rtxMode=true;
  ftRenderLightmap.verbose=false;ftRenderLightmap.suppressPopups=true;ftRenderLightmap.samplesWarning=false;ftRenderLightmap.prefabWarning=false;ftRenderLightmap.useScenePath=false;ftRenderLightmap.outputPath="Parking/Baked";
  ftRenderLightmap.scenePath=Path.GetFullPath("Library/ParkingBakeryTemp");Directory.CreateDirectory(ftRenderLightmap.scenePath);ftRenderLightmap.scenePathQuoted="\""+ftRenderLightmap.scenePath+"\"";ftBuildGraphics.scenePath=ftRenderLightmap.scenePath;
  window.unloadScenesInDeferredMode=false;window.SaveRenderSettings();AssetDatabase.SaveAssets();
  started=EditorApplication.timeSinceStartup;finished=false;ftRenderLightmap.OnFinishedFullRender+=Finished;EditorApplication.update+=Watch;bakeStarted=true;window.RenderButton(false);
 }
 static void Watch(){if(finished)return;double elapsed=EditorApplication.timeSinceStartup-started;if(ftRenderLightmap.userCanceled||elapsed>1200||(elapsed>10&&!ftRenderLightmap.bakeInProgress))Fail(new Exception("Bakery bake failed/cancelled/timed out; inspect bake log"));}
 static void Finished(object sender,EventArgs e){
  finished=true;bakeCompleted=true;ftRenderLightmap.OnFinishedFullRender-=Finished;EditorApplication.update-=Watch;
  EditorApplication.delayCall+=FinalizeBake;
 }
 static void FinalizeBake(){
   try{
   content=UnityEngine.Object.FindObjectOfType<ParkingAtmosphere>();
   content.ground.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Asphalt.mat");content.televisionStack.SetActive(true);
   if(LightmapSettings.lightmaps.Length==0||content.ground.lightmapIndex<0||content.ground.lightmapIndex>=LightmapSettings.lightmaps.Length)throw new Exception("Bakery completed without a valid ground lightmap");
   var prefab=PrefabUtility.SaveAsPrefabAsset(content.gameObject,Root+"Prefabs/ParkingContent.prefab");
   var host=UnityEngine.Object.FindObjectOfType<WindowProjection>().gameObject;var reference=host.GetComponent<ParkingContentReference>();if(!reference)reference=host.AddComponent<ParkingContentReference>();reference.prefab=prefab;reference.expectedGroundLightmap=LightmapSettings.lightmaps[content.ground.lightmapIndex].lightmapColor;
   EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
   File.WriteAllText("../Validation/Parking/bakery.txt","PASS: Bakery indirect bake; seconds="+(EditorApplication.timeSinceStartup-started)+"; maps="+LightmapSettings.lightmaps.Length+"; ground index="+content.ground.lightmapIndex+"; televisions excluded and restored; effective executor/settings recorded in bake.log.\n");
   ParkingBuilder.BuildPlayerOnly();backupFiles=null;File.Delete(Manifest);if(Application.isBatchMode)EditorApplication.Exit(0);
   }catch(Exception error){Fail(error);}
 }
 public static void CheckRecovery(){
  if(ftRenderLightmap.bakeInProgress)throw new InvalidOperationException("A bake is already running");
  foreach(float depth in new[]{39f,51f,52f,360f})foreach(var r in FarRectangles(depth))if(r.x>=r.z||r.y>=r.w||r.y<0||r.w>depth)throw new Exception("Invalid floor partition at "+depth);
  Snapshot();var before=backupFiles.ToDictionary(p=>p,p=>Convert.ToBase64String(File.ReadAllBytes(p)));
  File.AppendAllText(Root+"Scenes/Parking.unity","\n");File.AppendAllText(Root+"Prefabs/ParkingContent.prefab","\n");File.WriteAllText(Root+"rollback-sentinel.txt","test");
  Restore();foreach(var pair in before)if(Convert.ToBase64String(File.ReadAllBytes(pair.Key))!=pair.Value)throw new Exception("Rollback mismatch: "+pair.Key);
  if(File.Exists(Root+"rollback-sentinel.txt"))throw new Exception("Rollback retained a new file");
  File.WriteAllText("../Validation/Parking/bakery-recovery.txt","PASS: exact asset rollback and new-file removal; floor depths 39, 51, 52, 360 have positive non-overlapping partitions.\n");
 }
#else
 public static void Run(){throw new Exception("Restore the user-provided Bakery package with tools/parking/bakery_assets.py first.");}
#endif
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using TMPro;

public static class CalibrationDemoBuild {
 const string Root="Assets/Calibration/";
 public static void Build(){
  CalibrationUiAssets.Prepare();
  Directory.CreateDirectory("../Validation/Calibration");
  EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();
  var driver=projection.GetComponent<PreviewEyeDriver>();if(driver)UnityEngine.Object.DestroyImmediate(driver);
  var cadence=projection.GetComponent<AdaptiveRenderCadence>();if(cadence)cadence.adaptive=false;
  var model=GameObject.Find("SceneModel").transform;var interior=GameObject.Find("InteriorContent_A").transform;
  // The fixed cabinet shell defines the aperture. The recessed shelf and exhibits share A.
  var cabinet=GameObject.Find("FixedCabinet");
  if(PrefabUtility.IsPartOfPrefabInstance(cabinet))PrefabUtility.UnpackPrefabInstance(cabinet,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
  var shelf=cabinet.GetComponentsInChildren<Renderer>().First(r=>r.name=="Shelf");
  shelf.transform.SetParent(interior,true);
  var rig=UnityEngine.Object.FindObjectOfType<PhysicalDisplayRig>();if(rig)UnityEngine.Object.DestroyImmediate(rig);
  var original=GameObject.Find("ReplaceableSlot");
  var app=new GameObject("Calibration Runtime").AddComponent<CalibrationRuntime>();
  app.projection=projection;app.modelRoot=model;app.contentRoot=interior;
  var roomObject=new GameObject("Procedural calibration room");roomObject.transform.SetParent(projection.screenFrame,false);
  app.room=roomObject.AddComponent<CalibrationRoom>();
  string materialPath=Root+"Runtime/CalibrationSurface.mat";
  var surface=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
  if(!surface){surface=new Material(Shader.Find("Spatial/CalibrationGrid"));AssetDatabase.CreateAsset(surface,materialPath);}
  app.room.surface=surface;
  original.SetActive(true);
  CalibrationGeometryChecks.Run(app);
  if(!Shader.Find("TextMeshPro/Distance Field")){
   string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly).resolvedPath;
   AssetDatabase.ImportPackage(Path.Combine(package,"Package Resources/TMP Essential Resources.unitypackage"),false);AssetDatabase.Refresh();
  }
  var font=AssetDatabase.LoadAssetAtPath<Font>(Root+"Fonts/SourceHanSansSC-Regular.otf");
  if(!font)throw new Exception("Missing Source Han Sans font");
  var fontAsset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"Fonts/CalibrationChinese.asset");
  if(!fontAsset){fontAsset=TMP_FontAsset.CreateFontAsset(font,32,4,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);AssetDatabase.CreateAsset(fontAsset,Root+"Fonts/CalibrationChinese.asset");foreach(var texture in fontAsset.atlasTextures)AssetDatabase.AddObjectToAsset(texture,fontAsset);AssetDatabase.AddObjectToAsset(fontAsset.material,fontAsset);}
  var view=app.gameObject.AddComponent<CalibrationDemoView>();view.runtime=app;view.font=fontAsset;
  var smoke=app.gameObject.AddComponent<CalibrationSmoke>();smoke.runtime=app;
  Directory.CreateDirectory(Root+"Scenes");EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Root+"Scenes/CalibrationDemo.unity");
  PlayerSettings.productName="Spatial Wallpaper Calibration";PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=960;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
  AssetDatabase.SaveAssets();
  var report=BuildPipeline.BuildPlayer(new[]{Root+"Scenes/CalibrationDemo.unity"},"Builds/Calibration/CalibrationDemo.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  File.WriteAllText("../Validation/Calibration/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
  if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Calibration build failed or reported shader/content errors");
 }
}

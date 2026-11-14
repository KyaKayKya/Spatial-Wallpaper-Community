using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
public static class DesktopBuild {
 // Rebuild code against the exact prepared scenes without regenerating assets.
 public static void BuildPrepared(){
  var scenes=new[]{"Assets/Library/Scenes/Library.unity"}.Concat(SpatialWallpaper.Library.ArtworkCatalog.Builtins.Select(work=>work.ScenePath)).Concat(new[]{SpatialWallpaper.Library.ArtworkCatalog.BlueprintTemplate.ScenePath}).ToArray();
  scenes=new[]{SpatialWallpaper.Desktop.Editor.NativeEntryBuild.Prepare()}.Concat(scenes).ToArray();
  string single=Environment.GetEnvironmentVariable("SPATIAL_PREPARED_SCENE");if(!string.IsNullOrEmpty(single))scenes=new[]{single};
  foreach(string scene in scenes)if(!File.Exists(scene))throw new FileNotFoundException("Prepared library scene missing",scene);
  string output=Environment.GetEnvironmentVariable("SPATIAL_PREPARED_OUTPUT")??"Builds/Native-Task0/SpatialWallpaper.exe";
  var report=BlueprintBuild.BuildPlayer(scenes,output,BuildTarget.StandaloneWindows64);
  if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Prepared desktop build failed");
 }
 public static void Build(){
  bool splash=PlayerSettings.SplashScreen.show,logo=PlayerSettings.SplashScreen.showUnityLogo;
  Environment.SetEnvironmentVariable("SPATIAL_DESKTOP_BUILD","true");
  try{PlayerSettings.SplashScreen.show=false;PlayerSettings.SplashScreen.showUnityLogo=false;
   Directory.CreateDirectory("../Validation/Desktop");
   bool disabled=!PlayerSettings.SplashScreen.show&&!PlayerSettings.SplashScreen.showUnityLogo;
   File.WriteAllText("../Validation/Desktop/splash-settings.json",new JObject{["requested_disabled"]=true,["disabled"]=disabled,["show"]=PlayerSettings.SplashScreen.show,["show_unity_logo"]=PlayerSettings.SplashScreen.showUnityLogo}.ToString());
   if(!disabled)UnityEngine.Debug.LogWarning("Unity license enforces the splash screen; recorded in splash-settings.json. No logo removal is claimed.");
   LibraryBuild.Build();
  }finally{PlayerSettings.SplashScreen.show=splash;PlayerSettings.SplashScreen.showUnityLogo=logo;Environment.SetEnvironmentVariable("SPATIAL_DESKTOP_BUILD",null);}
 }
}

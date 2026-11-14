using UnityEngine;
using UnityEditor;
public static class ShowcaseMenu {
 [MenuItem("Spatial Wallpaper/Validate projection")]
 static void Validate(){Debug.Log(ShowcaseChecks.Run());}
 [MenuItem("Spatial Wallpaper/Install baked study into open Showcase scene")]
 static void Install(){Debug.Log(BakedStudyBuilder.Install());}
 [MenuItem("Spatial Wallpaper/Bake empty reflection")]
 static void Reflection(){Debug.Log(BakedStudyBuilder.Reflection());}
 [MenuItem("Spatial Wallpaper/Build Windows preview")]
 static void Build(){Debug.Log(ShowcaseBuilder.Build());}
}

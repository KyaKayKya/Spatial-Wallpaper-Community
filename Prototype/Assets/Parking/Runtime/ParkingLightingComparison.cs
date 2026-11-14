using System;
using UnityEngine;
namespace SpatialWallpaper.Parking {
 // Diagnostic A/B only. Keeps all textures resident, so it measures sampling cost, not memory savings.
 [DefaultExecutionOrder(1000)]
 public sealed class ParkingLightingComparison:MonoBehaviour {
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--parking-no-baked-lighting")>=0)new GameObject("LightingComparison").AddComponent<ParkingLightingComparison>();}
  void Start(){foreach(var renderer in FindObjectOfType<ParkingAtmosphere>().GetComponentsInChildren<Renderer>(true))renderer.lightmapIndex=-1;}
 }
}

using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class AudioSample:MonoBehaviour {
 public Material surface;
 public float lowBand;
 void Update(){lowBand=SpatialWallpaper.Sdk.Audio.GetBand(0);surface.SetColor("_BaseColor",new Color(.1f+lowBand*.8f,.2f+lowBand*.5f,.6f,1));}
}

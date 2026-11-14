using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class NativeArtworkSound:MonoBehaviour {
 public AudioClip music,rain;public bool hasRain;
 void Start(){Sound.PlayMusic(music,1);if(hasRain)Sound.PlayMusic(rain,.55f);}
}

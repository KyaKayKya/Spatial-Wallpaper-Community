using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class WallpaperSound:MonoBehaviour {
 public AudioClip music;
 void Start(){Sound.PlayMusic(music,1);}
}

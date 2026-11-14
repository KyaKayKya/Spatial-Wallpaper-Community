using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]public sealed class StageCNullAuthor:MonoBehaviour {
 public StageCNullData missing;public int counter;
 void Start(){counter=missing.Constant();}
}
[System.Serializable,WallpaperScript]public sealed class StageCNullData {public int Constant(){return 9;}}

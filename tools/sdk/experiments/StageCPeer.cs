using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript] public sealed class StageCPeer:MonoBehaviour {
 public StageCAuthor primary;public int value;
 void Start(){value=20;}void Update(){value+=primary.data.value-2;}
}
[System.Serializable,WallpaperScript] public sealed class StageCData {public int value=3;}


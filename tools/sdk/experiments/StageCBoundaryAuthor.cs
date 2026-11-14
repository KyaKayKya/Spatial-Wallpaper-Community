using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]public sealed class StageCBoundaryAuthor:MonoBehaviour {
 public Transform target;
 void Start(){target.localPosition=new Vector3(100001f,0f,0f);}
}

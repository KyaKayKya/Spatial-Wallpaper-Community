using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]public sealed class StageCSpawn:MonoBehaviour {
 public Transform target;public int count;
 void Start(){count=10;target.localPosition=new Vector3(0,5f,0);}
 void Update(){count++;target.Rotate(0,1f,0);}
}

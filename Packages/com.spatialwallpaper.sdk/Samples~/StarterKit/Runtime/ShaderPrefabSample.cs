using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class ShaderPrefabSample:MonoBehaviour {
 public Material surface;
 public Prefab prefab;
 public GameObject clone;
 public int frame;
 void Start(){clone=Objects.Instantiate(prefab);}
 void Update(){
  frame=frame+1;surface.SetFloat("_Pulse",(frame%120)/120f);
  if(frame==180)Objects.Destroy(clone);
  if(frame==240){clone=Objects.Instantiate(prefab);frame=0;}
 }
}

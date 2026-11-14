using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]public sealed class StageENativeInputAuthor:MonoBehaviour {
 public bool focused,held,unstable;public int downs,ups,updates;public float x,y;
 void Update(){
  updates=updates+1;focused=SpatialWallpaper.Sdk.Input.IsFocused();held=SpatialWallpaper.Sdk.Input.GetButton("Space");x=SpatialWallpaper.Sdk.Input.GetPointerX();y=SpatialWallpaper.Sdk.Input.GetPointerY();
  bool down=SpatialWallpaper.Sdk.Input.GetButtonDown("Space"),up=SpatialWallpaper.Sdk.Input.GetButtonUp("Space");
  if(down)downs=downs+1;if(up)ups=ups+1;
  if(down!=SpatialWallpaper.Sdk.Input.GetButtonDown("Space"))unstable=true;if(up!=SpatialWallpaper.Sdk.Input.GetButtonUp("Space"))unstable=true;
 }
}

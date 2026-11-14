using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEInputAuthor:MonoBehaviour {
 public bool held,down,up,focused;
 public float x,y,wheel;
 void Update(){
  held=SpatialWallpaper.Sdk.Input.GetButton("Space");
  down=SpatialWallpaper.Sdk.Input.GetButtonDown("Space");
  up=SpatialWallpaper.Sdk.Input.GetButtonUp("Space");
  focused=SpatialWallpaper.Sdk.Input.IsFocused();
  x=SpatialWallpaper.Sdk.Input.GetPointerX();
  y=SpatialWallpaper.Sdk.Input.GetPointerY();
  wheel=SpatialWallpaper.Sdk.Input.GetWheel();
 }
}

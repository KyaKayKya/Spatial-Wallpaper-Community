using System;
using UnityEngine;
namespace SpatialWallpaper.Sdk {
 // Values use the same author coordinates as content. The camera stays trusted.
 public static class View {
  static WindowProjection play;
  internal static void BindPlay(WindowProjection projection){play=projection;}
  static WindowProjection Current()=>play?play:throw new InvalidOperationException("view-unavailable");
  public static Vector3 GetEye()=>Current().EyeScreen;
  public static Vector3 GetSize(){var value=Current();return new Vector3(value.displayWidth,value.displayHeight,0);}
 }
}

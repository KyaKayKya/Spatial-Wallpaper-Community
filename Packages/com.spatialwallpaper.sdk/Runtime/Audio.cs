using System;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk {
 public static class Audio {
  static CapabilityCalls play;
  internal static void BindPlay(CapabilityCalls calls){play=calls;}
  public static float GetBand(int index)=>(play??throw new InvalidOperationException("SDK ordinary Play capability session is not running")).AudioBand(index);
 }
}

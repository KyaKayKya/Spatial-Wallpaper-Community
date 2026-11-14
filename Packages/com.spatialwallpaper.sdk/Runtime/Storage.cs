using System;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk {
 // Author surface. Player calls are normalized to per-instance CapabilityBindings, never this static state.
 public static class Storage {
  static CapabilityCalls play;
  internal static void BindPlay(CapabilityCalls calls){play=calls;}
  static CapabilityCalls Current=>play??throw new InvalidOperationException("SDK ordinary Play capability session is not running");
  public static int BeginRead(string key)=>Current.BeginRead(key);
  public static int BeginWriteText(string key,string value,string version)=>Current.BeginWriteText(key,value,version);
  public static int State(int request)=>Current.State(request);
  public static string Version(int request)=>Current.Version(request);
  public static string Error(int request)=>Current.Error(request);
  public static string TakeText(int request)=>Current.TakeText(request);
  public static bool Release(int request)=>Current.Release(request);
 }
}

using System;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk {
 public static class Llm {
  static CapabilityCalls play;
  internal static void BindPlay(CapabilityCalls calls){play=calls;}
  static CapabilityCalls Current=>play??throw new InvalidOperationException("SDK ordinary Play capability session is not running");
  public static int BeginPrompt(string prompt)=>Current.BeginPrompt(prompt);
  public static int BeginCancel(int request)=>Current.BeginCancel(request);
  public static int State(int request)=>Current.State(request);
  public static string Error(int request)=>Current.Error(request);
  public static string TakeText(int request)=>Current.TakeText(request);
  public static bool Release(int request)=>Current.Release(request);
 }
}

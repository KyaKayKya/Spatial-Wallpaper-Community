using System;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk {
 public static class Input {
  static CapabilityCalls play;
  internal static void BindPlay(CapabilityCalls calls){play=calls;}
  static CapabilityCalls Session()=>play??throw new InvalidOperationException("SDK ordinary Play capability session is not running");
  public static bool GetButton(string name)=>Session().InputButton(name);
  public static bool GetButtonDown(string name)=>Session().InputDown(name);
  public static bool GetButtonUp(string name)=>Session().InputUp(name);
  public static bool IsFocused()=>Session().InputFocused();
  public static float GetPointerX()=>Session().InputX();
  public static float GetPointerY()=>Session().InputY();
  public static float GetWheel()=>Session().InputWheel();
 }
}

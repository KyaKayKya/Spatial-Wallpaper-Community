using System;
// Compatibility marker retained for the pinned Editor converter.
[AttributeUsage(AttributeTargets.Class,Inherited=true)]
public class CilboxableAttribute:Attribute {}
namespace SpatialWallpaper.Sdk {
 [AttributeUsage(AttributeTargets.Class,Inherited=true)]
 public sealed class WallpaperScriptAttribute:CilboxableAttribute {}
}

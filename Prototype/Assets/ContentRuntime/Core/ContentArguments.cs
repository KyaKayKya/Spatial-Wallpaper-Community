using System;
namespace SpatialWallpaper.Content {
 public static class ContentArguments {
  public static string Argument(string name){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:null;}
 }
}

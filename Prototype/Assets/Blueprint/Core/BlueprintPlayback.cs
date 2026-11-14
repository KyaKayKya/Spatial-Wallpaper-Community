using System;
using System.IO;
using System.Globalization;
namespace SpatialWallpaper.Blueprint {
 public static class BlueprintPlayback {
  static string FilePath(string root,string work,string revision){if(!Guid.TryParseExact(work,"N",out _)||!Guid.TryParseExact(revision,"N",out _))throw new BlueprintImportException("playback-id");return Path.Combine(root,"playback",work+"-"+revision+".phase");}
  public static float Read(string root,string work,string revision,float fallback){string path=FilePath(root,work,revision);if(!File.Exists(path)||new FileInfo(path).Length>64)return fallback;return float.TryParse(File.ReadAllText(path),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)&&!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<24?value:fallback;}
  public static void Write(string root,string work,string revision,float hour){if(float.IsNaN(hour)||float.IsInfinity(hour))throw new BlueprintImportException("playback-phase");string path=FilePath(root,work,revision);Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,((hour%24+24)%24).ToString("R",CultureInfo.InvariantCulture));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 }
}

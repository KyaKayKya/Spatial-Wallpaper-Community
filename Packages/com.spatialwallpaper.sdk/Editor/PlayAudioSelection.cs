using System;using System.IO;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Editor {
 // Project-local trusted choice. Source identity is never read from author data.
 internal static class PlayAudioSelection {
  internal static string PathName=>Path.GetFullPath("Library/SpatialWallpaper/audio-source.json");
  internal static JObject Read(){if(!File.Exists(PathName))return null;var file=new FileInfo(PathName);if(file.Length>1024)throw new InvalidDataException("audio-selection-settings-size");return TrustedAudioSource.Validate(JObject.Parse(File.ReadAllText(PathName)));}
  internal static void Select(JToken source){var value=TrustedAudioSource.RequireLive(source);if(value==null){if(File.Exists(PathName))File.Delete(PathName);return;}Directory.CreateDirectory(Path.GetDirectoryName(PathName));File.WriteAllText(PathName,value.ToString(Newtonsoft.Json.Formatting.None));}
 }
}

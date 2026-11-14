using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Text;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk;
namespace SpatialWallpaper.Content {
 // A bounded routing hint only. Neither branch may admit content from this
 // result: legacy still uses PackageValidator, modern uses restricted Validator.
 public static class ContentPackageFormat {
  public static bool IsLegacy(string path)=>Schema(path)==2;
  public static bool IsNative(string path)=>Schema(path)==5;
  static int Schema(string path){var file=new FileInfo(path);JsonData.Require(file.Exists&&file.Length>0&&file.Length<=PackageValidator.MaxTotal&&(file.Attributes&FileAttributes.ReparsePoint)==0,"package-file");using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var archive=new ZipArchive(input,ZipArchiveMode.Read)){var entries=archive.Entries.Where(e=>e.FullName=="manifest.json").Take(2).ToArray();JsonData.Require(entries.Length==1,"package-manifest-count");var entry=entries[0];JsonData.Require(entry.Length>0&&entry.Length<=PackageValidator.MaxFile,"package-manifest-size");using(var stream=entry.Open())using(var bytes=new MemoryStream()){var buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){JsonData.Require(bytes.Length+count<=entry.Length,"package-manifest-expansion");bytes.Write(buffer,0,count);}JsonData.Require(bytes.Length==entry.Length,"package-manifest-truncated");var manifest=JsonData.Parse(new UTF8Encoding(false,true).GetString(bytes.ToArray()));JsonData.Require(manifest["schema"]?.Type==JTokenType.Integer,"package-schema-type");return (int)manifest["schema"];}}}
 }
}

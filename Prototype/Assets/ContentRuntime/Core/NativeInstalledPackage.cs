using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Native;
namespace SpatialWallpaper.Content {
 // Both native formats retain the same installed work/revision lifecycle. Only
 // platform packages may resolve a trusted product scene; external packages
 // carry admitted canonical data and never supply a Scene/Prefab bundle.
 public sealed class NativeInstalledPackage {
  public const string ExternalSceneId="external-native-data";
  readonly NativePackage platform;public NativeExternalPackage External{get;}
  public string ArchivePath{get;}public string ArchiveSha256{get;}
  public bool IsExternal=>External!=null;
  public string BuildId=>IsExternal?External.BuildId:platform.BuildId;
  public string WorkId=>IsExternal?External.WorkId:platform.WorkId;
  public string Revision=>IsExternal?External.Revision:platform.Revision;
  public string SceneId=>IsExternal?ExternalSceneId:platform.SceneId;
  public string LibraryDigest=>IsExternal?null:platform.LibraryDigest;
  public string SceneSourceDigest=>IsExternal?null:platform.SceneSourceDigest;
  public JObject Manifest=>IsExternal?External.Manifest:platform.Manifest;
  public byte[] ScriptDefinition=>IsExternal?External.ScriptDefinition:platform.ScriptDefinition;
  NativeInstalledPackage(string path,string digest,NativePackage platform,NativeExternalPackage external){ArchivePath=path;ArchiveSha256=digest;this.platform=platform;External=external;}
  public static NativeInstalledPackage Validate(string path,NativePlatformCatalog catalog=null){
   path=Path.GetFullPath(path);var file=new FileInfo(path);JsonData.Require(file.Exists&&file.Length>0&&file.Length<=PackageValidator.MaxTotal,"native-installed-archive-size");
   // Pin immutable input across routing, admission and the archive digest.
   using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    NativePackage platform=null;NativeExternalPackage external=null;
    if(NativeExternalPackage.IsExternal(path))external=NativeExternalPackage.Validate(path);
    else {platform=NativePackage.Validate(path);if(catalog!=null)catalog.Require(platform);}
    string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
    return new NativeInstalledPackage(path,digest,platform,external);
   }
  }
  public void WriteArchive(Stream output){if(IsExternal)External.WriteArchive(output);else platform.WriteArchive(output);}
 }
}

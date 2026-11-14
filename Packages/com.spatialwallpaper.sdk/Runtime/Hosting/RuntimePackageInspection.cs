namespace SpatialWallpaper.Sdk.Hosting {
 // Created only by the authenticated, identity-checked manager client. A
 // receipt binds the inspected immutable archive, not any later staging bytes.
 public sealed class RuntimePackageInspection {
  public const long MaxArchiveBytes=64L*1024*1024;
  public string BuildId{get;}public string ArchiveSha256{get;}readonly string[] capabilities;public string[] Capabilities=>(string[])capabilities.Clone();
  internal RuntimePackageInspection(string build,string digest,string[] tags){BuildId=build;ArchiveSha256=digest;capabilities=(string[])tags.Clone();}
 }
}

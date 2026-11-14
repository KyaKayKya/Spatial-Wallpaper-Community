using System;using System.IO;
namespace SpatialWallpaper.Content {
 // A locked archive already imported from a restricted-Validator receipt.
 // The runtime still performs its own admission before starting a Worker.
 public sealed class SandboxContentBuild:IDisposable {
  readonly FileStream snapshot;public string Path{get;}public string BuildId{get;}public string ArchiveSha256{get;}public Guid Installation{get;}public string RevisionId{get;}readonly string[] capabilities;public string[] Capabilities=>(string[])capabilities.Clone();
  internal SandboxContentBuild(string path,string build,string digest,Guid installation,string revision,string[] tags,FileStream locked){Path=path;BuildId=build;ArchiveSha256=digest;Installation=installation;RevisionId=revision;capabilities=(string[])tags.Clone();snapshot=locked;}
  public void Dispose(){snapshot.Dispose();}
 }
}

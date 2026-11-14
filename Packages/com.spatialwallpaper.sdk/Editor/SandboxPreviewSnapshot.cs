using System;using System.IO;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 internal sealed class SandboxPreviewSnapshot:IDisposable {
  readonly byte[] bytes;readonly FileStream retained;internal readonly string Path,BuildId,ArchiveDigest,DataDirectory;internal readonly string[] Capabilities;internal bool Presented;
  static string Digest(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
  SandboxPreviewSnapshot(string path,string data,string expectedDigest=null,string expectedBuild=null){
   Path=System.IO.Path.GetFullPath(path);DataDirectory=System.IO.Path.GetFullPath(data);retained=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read);
   try{if(retained.Length<1||retained.Length>64*1024*1024)throw new InvalidDataException("preview-snapshot-size");bytes=new byte[checked((int)retained.Length)];int offset=0;while(offset<bytes.Length){int count=retained.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}ArchiveDigest=Digest(bytes);var admitted=Admission.Admit(bytes,"windows-x86_64");BuildId=admitted.BuildId;Capabilities=admitted.CapabilityTags;if(expectedDigest!=null&&ArchiveDigest!=expectedDigest||expectedBuild!=null&&BuildId!=expectedBuild)throw new InvalidDataException("preview-recovery-snapshot-drift");}catch{retained.Dispose();throw;}
  }
  internal static SandboxPreviewSnapshot Create(SceneDefinition definition,string data){
   if(!definition||!definition.contentRoot)throw new InvalidDataException("preview-scene-definition-required");var rules=RuleCatalog.Current.Snapshot;if(Application.unityVersion!=(string)rules["engine"])throw new InvalidDataException("preview-editor-version");
   if(QualitySettings.activeColorSpace!=ColorSpace.Linear)throw new InvalidDataException("preview-linear-color-space-required");
   if(string.IsNullOrWhiteSpace(definition.licenseAttribution))throw new InvalidDataException("preview-license-attribution-required");
   var licenses=new JArray(new JObject{{"name",definition.displayName},{"source",definition.licenseSource},{"license",definition.licenseName},{"attribution",definition.licenseAttribution}});byte[] bytes=ScriptSceneExporter.Export(definition.contentRoot.gameObject,definition.authorFrame,licenses);
   data=System.IO.Path.GetFullPath(data);SandboxPreviewCache.RequireSnapshotCapacity(data);string directory=System.IO.Path.Combine(data,"snapshots",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string path=System.IO.Path.Combine(directory,"scene.spatial");try{using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}return new SandboxPreviewSnapshot(path,data);}catch{SandboxPreviewCache.RemoveSnapshot(data,path);throw;}
  }
  internal static SandboxPreviewSnapshot Restore(JObject record)=>new SandboxPreviewSnapshot((string)record["snapshot"],(string)record["data"],(string)record["archive_sha256"],(string)record["build"]){Presented=(bool?)record["presented"]??false};
  internal JObject Record()=>new JObject{{"snapshot",Path},{"data",DataDirectory},{"archive_sha256",ArchiveDigest},{"build",BuildId},{"presented",Presented}};
  internal void Retire(){Dispose();SandboxPreviewCache.RemoveSnapshot(DataDirectory,Path);}
  internal void Export(string target){
   if(!Presented)throw new InvalidOperationException("preview-snapshot-not-presented");target=System.IO.Path.GetFullPath(target);if(string.Equals(target,Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("preview-snapshot-is-owned");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));string temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";
   try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  public void Dispose()=>retained.Dispose();
 }
}

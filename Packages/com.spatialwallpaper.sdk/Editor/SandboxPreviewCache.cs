using System;using System.Diagnostics;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Editor {
 internal static class SandboxPreviewCache {
  internal static void NoLinks(string path){for(var entry=new DirectoryInfo(Path.GetFullPath(path));entry!=null;entry=entry.Parent)if(entry.Exists&&(entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("preview-cache-link");}
  static bool GuidDirectory(string path)=>Guid.TryParseExact(Path.GetFileName(path),"N",out var id)&&id!=Guid.Empty;
  internal static void RequireSnapshotCapacity(string data){string root=Path.Combine(data,"snapshots");NoLinks(root);Directory.CreateDirectory(root);if(Directory.EnumerateFileSystemEntries(root).Count()>=2)throw new InvalidOperationException("preview-snapshot-cache-unproven-entries");}
  internal static void RemoveSnapshot(string data,string path){
   path=Path.GetFullPath(path);string directory=Path.GetDirectoryName(path),root=Path.GetFullPath(Path.Combine(data,"snapshots"));if(!GuidDirectory(directory)||!string.Equals(Path.GetDirectoryName(directory),root,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="scene.spatial")throw new InvalidDataException("preview-snapshot-cache-boundary");NoLinks(directory);
   if(!Directory.Exists(directory))return;if(Directory.EnumerateFileSystemEntries(directory).Any(p=>!string.Equals(p,path,StringComparison.OrdinalIgnoreCase))||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("preview-snapshot-cache-unexpected-entry");File.Delete(path);Directory.Delete(directory,false);
  }
  static bool Ended(int id,long creation){try{using(var process=Process.GetProcessById(id))return PinnedProcessOwner.ReadCreationFileTime(process.Handle)!=creation||process.HasExited;}catch(ArgumentException){return true;}}
  internal static void Completed(string workspace){string launch=Path.GetDirectoryName(workspace);if(!GuidDirectory(launch)||Path.GetFileName(workspace)!="native")throw new InvalidDataException("preview-launch-cache-boundary");NoLinks(workspace);File.WriteAllText(Path.Combine(launch,"completed.json"),new JObject{{"format",1},{"launch",Path.GetFileName(launch)}}.ToString());}
  internal static void TrimLaunches(string data){
   string root=Path.Combine(data,"launches");NoLinks(root);Directory.CreateDirectory(root);var completed=Directory.GetDirectories(root).Where(p=>GuidDirectory(p)&&File.Exists(Path.Combine(p,"completed.json"))).OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
   foreach(string launch in completed.Skip(16)){
    NoLinks(launch);string marker=Path.Combine(launch,"completed.json");if(new FileInfo(marker).Length>256||(File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("preview-cache-marker");var proof=JObject.Parse(File.ReadAllText(marker));if(proof.Count!=2||(int?)proof["format"]!=1||(string)proof["launch"]!=Path.GetFileName(launch))throw new InvalidDataException("preview-cache-marker");
    string native=Path.Combine(launch,"native"),statusPath=Path.Combine(native,"status.json");NoLinks(native);if(new FileInfo(statusPath).Length>32768)throw new InvalidDataException("preview-cache-status");var identity=JObject.Parse(File.ReadAllText(statusPath));int viewer=(int)identity["viewer_pid"];long creation=(long)identity["viewer_creation_filetime"];var status=PreviewStatusFile.Read(statusPath,viewer,creation);
    if(status==null||!Ended(viewer,creation)||!(bool)status["window_resources_released"]||!(bool)status["host_resources_released"]||(bool)status["forced_host_termination"]||(int)status["host_pid"]>0&&!Ended((int)status["host_pid"],(long)status["host_creation_filetime"]))throw new InvalidOperationException("preview-cache-cleanup-unproven");
    string[] files={marker,Path.Combine(launch,"preview.json"),statusPath,Path.Combine(native,"status.json.tmp"),Path.Combine(native,"host-config.json"),Path.Combine(native,"stop-request")};
    if(Directory.EnumerateFileSystemEntries(launch).Any(p=>p!=native&&!files.Contains(p))||Directory.EnumerateFileSystemEntries(native).Any(p=>!files.Contains(p)))throw new InvalidDataException("preview-cache-unexpected-entry");foreach(string path in files)if(File.Exists(path)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("preview-cache-link");File.Delete(path);}Directory.Delete(native,false);Directory.Delete(launch,false);
   }
   if(Directory.EnumerateFileSystemEntries(root).Count()>=64)throw new InvalidOperationException("preview-launch-cache-unproven-entries");
  }
 }
}

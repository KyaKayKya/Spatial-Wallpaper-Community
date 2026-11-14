using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
namespace SpatialWallpaper.Sdk.Security {
 // Editor/export and client/import call exactly the same admission policy.
 public static class PackageWriter {
  public static byte[] Export(JObject program,JObject content,IDictionary<string,byte[]> resources,JArray licenses,JObject renderProfile=null){
   var files=new Dictionary<string,byte[]>(StringComparer.Ordinal){
    {"program.json",Encoding.UTF8.GetBytes(program.ToString(Formatting.None))},
    {"content.json",Encoding.UTF8.GetBytes(content.ToString(Formatting.None))}
   };
   foreach(var pair in resources){JsonData.Require(pair.Key.StartsWith("resources/",StringComparison.Ordinal),"resource-path");files.Add(pair.Key,(byte[])pair.Value.Clone());}
   var catalog=RuleCatalog.Current.Snapshot;var manifest=new JObject();
   foreach(string key in new[]{"schema","sdk","vm","engine","pipeline","backend"})manifest[key]=catalog[key].DeepClone();
   manifest["rules"]=RuleCatalog.Current.Id;manifest["rules_sha256"]=RuleCatalog.Current.Digest;manifest["platform"]="windows-x86_64";
   var profile=renderProfile??RenderProfiles.DefaultIdentity;RenderProfiles.Require(profile);manifest["render_profile"]=profile.DeepClone();
   var records=new JObject();foreach(var file in files.OrderBy(f=>f.Key,StringComparer.Ordinal))records[file.Key]=new JObject{{"size",file.Value.Length},{"sha256",PackageValidator.Digest(file.Value)}};
   manifest["files"]=records;manifest["licenses"]=licenses.DeepClone();manifest["build_id"]=PackageValidator.Identity(manifest);
   files.Add("manifest.json",Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None)));
   using(var memory=new MemoryStream()){
    using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var file in files.OrderBy(f=>f.Key,StringComparer.Ordinal)){
     using(var stream=zip.CreateEntry(file.Key,CompressionLevel.Optimal).Open())stream.Write(file.Value,0,file.Value.Length);
    }
    byte[] snapshot=memory.ToArray();Admission.Admit(snapshot,"windows-x86_64");return snapshot;
   }
  }
 }
}

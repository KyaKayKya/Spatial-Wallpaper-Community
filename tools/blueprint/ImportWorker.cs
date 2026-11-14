using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Blueprint;
static class ImportWorker {
 static int Main(string[] args){try{if(args.Length!=3)throw new ArgumentException("import-worker-arguments");string source=Path.GetFullPath(args[0]),job=Path.GetFullPath(args[1]),cache=Path.GetFullPath(args[2]);var file=new FileInfo(source);if(!file.Exists||file.Length>NbtInput.MaxInputBytes)throw new BlueprintImportException("blueprint-file-budget");var token=CancellationToken.None;byte[] input=NbtInput.ReadFile(source,token);var document=BlueprintReader.Read(input,Path.GetFileName(source),token);var catalog=ModelCatalog.Load(File.ReadAllText(Path.Combine(job,"catalog.json")));string key=BlockMeshCompiler.CacheKeyFor(document.SourceHash,catalog.Fingerprint);long bytes;var compiled=BlueprintCacheFiles.Read(cache,key,out bytes);bool hit=compiled!=null;if(compiled==null){compiled=BlockMeshCompiler.Compile(document,catalog,token);bytes=BlueprintCacheFiles.Write(cache,compiled,token);}File.WriteAllBytes(Path.Combine(job,"document.bin"),BlueprintDocumentTransport.Write(document));File.WriteAllBytes(Path.Combine(job,"source.bin"),input);Console.WriteLine(new JObject{["key"]=key,["cache_hit"]=hit,["cache_bytes"]=bytes}.ToString(Newtonsoft.Json.Formatting.None));return 0;}catch(Exception e){Console.WriteLine(new JObject{["error"]=e.GetBaseException().Message}.ToString(Newtonsoft.Json.Formatting.None));return 2;}}
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace SpatialWallpaper.Blueprint {
 public static class BlueprintCacheFiles {
  public const long MaxTotalBytes=256L*1024*1024;
  static string PathFor(string root,string key){if(key==null||key.Length!=64||key.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new BlueprintImportException("cache-identity");return Path.Combine(root,key+".swbc");}
  public static CompiledBlueprint Read(string root,string key,out long bytes){bytes=0;string path=PathFor(root,key);try{var info=new FileInfo(path);if(!info.Exists)return null;if(info.Length>64L*1024*1024)throw new BlueprintImportException("cache-budget");var compiled=BlueprintCache.Read(File.ReadAllBytes(path),key);bytes=info.Length;File.SetLastWriteTimeUtc(path,DateTime.UtcNow);return compiled;}catch(IOException){try{File.Delete(path);}catch(IOException){}return null;}catch(UnauthorizedAccessException){return null;}}
  public static long Write(string root,CompiledBlueprint compiled,CancellationToken token){Directory.CreateDirectory(root);string path=PathFor(root,compiled.CacheKey),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";byte[] bytes=BlueprintCache.Write(compiled,token);try{File.WriteAllBytes(temp,bytes);token.ThrowIfCancellationRequested();if(File.Exists(path))File.Delete(path);File.Move(temp,path);var files=new DirectoryInfo(root).GetFiles("*.swbc").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();long total=0;for(int i=0;i<files.Length;i++){total+=files[i].Length;if((total>MaxTotalBytes||i>=128)&&files[i].FullName!=Path.GetFullPath(path))try{files[i].Delete();}catch(IOException){}}return bytes.Length;}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
}

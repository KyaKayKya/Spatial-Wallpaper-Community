using System;
using System.IO;
using System.Threading;
namespace SpatialWallpaper.Blueprint {
 public static class BlueprintSourceSnapshot {
  public static string Preserve(string root,string hash,string validatedSnapshot,CancellationToken token){var store=new BlueprintWorkspace(root);string target=store.SourcePath(hash),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{byte[] bytes=NbtInput.ReadFile(validatedSnapshot,token);File.WriteAllBytes(temp,bytes);token.ThrowIfCancellationRequested();if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);return target;}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
}

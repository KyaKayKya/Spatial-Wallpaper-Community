using System;using System.IO;using System.Collections.Generic;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 // Only generated runtime copies. Never deletes a distribution, installed package, or caller-selected subtree.
 internal sealed class OwnedAttempt:IDisposable {
  internal readonly string Root,Boundary;readonly string owner;bool disposed;
#if SDK_SUPERVISOR_TESTS
  internal static Action BeforeMarkerCreate,BeforeConstructorRollback;
#endif
  internal OwnedAttempt(string parent,Guid attempt,Action<Exception> cleanupFailure=null){
   if(attempt==Guid.Empty)throw new ArgumentException("attempt-identity");owner=attempt.ToString("N");Root=Path.GetFullPath(Path.Combine(parent,owner));Boundary=Path.GetFullPath(Path.Combine(parent,"boundary",owner));
   if(!string.Equals(Path.GetDirectoryName(Root),Path.GetFullPath(parent),StringComparison.OrdinalIgnoreCase)||Directory.Exists(Root)||File.Exists(Root)||Directory.Exists(Boundary)||File.Exists(Boundary))throw new InvalidDataException("attempt-directory-ownership");
   int count=0;foreach(string directory in Directory.EnumerateDirectories(parent))if(Path.GetFileName(directory)!="boundary"&&++count>=RuntimeLimits.CacheAttempts)throw new InvalidOperationException("attempt-cache-full-recovery-required");
   bool markerOwned=false;string marker=Path.Combine(Root,".sdk-worker-owned");try{Directory.CreateDirectory(Root);
#if SDK_SUPERVISOR_TESTS
   BeforeMarkerCreate?.Invoke();
#endif
   using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None)){markerOwned=true;using(var file=new StreamWriter(stream))file.Write(owner);}
   }catch{try{
#if SDK_SUPERVISOR_TESTS
    BeforeConstructorRollback?.Invoke();
#endif
    // Only this constructor's marker and an empty fresh GUID directory may be removed.
    // Extra files or links fail closed; never recursively remove a partially owned root.
    if(Directory.Exists(Root)){if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("attempt-constructor-cleanup-link");if(markerOwned&&File.Exists(marker)){if((File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("attempt-constructor-marker-link");File.Delete(marker);}Directory.Delete(Root,false);}
   }catch(Exception error){cleanupFailure?.Invoke(error);}throw;}
  }
  static void Inspect(string root){
   var paths=new List<string>{root};for(int i=0;i<paths.Count;i++){
    var attributes=File.GetAttributes(paths[i]);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("attempt-cleanup-link");
    if((attributes&FileAttributes.Directory)!=0)foreach(string path in Directory.GetFileSystemEntries(paths[i])){if(paths.Count>=32768)throw new InvalidDataException("attempt-cleanup-count");paths.Add(path);}
   }
  }
  public void Dispose(){
   if(disposed)return;
   if(Directory.Exists(Root)){Inspect(Root);if(File.ReadAllText(Path.Combine(Root,".sdk-worker-owned"))!=owner)throw new InvalidDataException("attempt-cleanup-owner");}
   if(Directory.Exists(Boundary))Inspect(Boundary);
   if(Directory.Exists(Root))Directory.Delete(Root,true);if(Directory.Exists(Boundary))Directory.Delete(Boundary,true);disposed=true;
  }
 }
}

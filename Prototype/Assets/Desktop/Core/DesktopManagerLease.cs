using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SpatialTracking;
namespace SpatialWallpaper.Desktop {
 public sealed class DesktopManagerLease:IDisposable {
  readonly RuntimeLease held;
  DesktopManagerLease(RuntimeLease stream){held=stream;}
  public static int ActiveOwner(string directory){
   string file=Path.Combine(directory,"manager.lock");if(!File.Exists(file))return 0;
   using(var probe=RuntimeLease.TryAcquire(file)){if(probe!=null)return 0;}
   using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input)){int.TryParse(reader.ReadToEnd(),out var owner);return owner;}
  }
  public static DesktopManagerLease TryAcquire(string directory,out int owner){
   Directory.CreateDirectory(directory);string file=Path.Combine(directory,"manager.lock");owner=0;
   var lease=RuntimeLease.TryAcquire(file);
   if(lease==null){using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input))int.TryParse(reader.ReadToEnd(),out owner);return null;}
   var stream=lease.Stream;
   owner=Process.GetCurrentProcess().Id;byte[] data=Encoding.ASCII.GetBytes(owner.ToString());stream.SetLength(0);stream.Write(data,0,data.Length);stream.Flush();return new DesktopManagerLease(lease);
  }
  public void Dispose(){held.Dispose();}
 }
}

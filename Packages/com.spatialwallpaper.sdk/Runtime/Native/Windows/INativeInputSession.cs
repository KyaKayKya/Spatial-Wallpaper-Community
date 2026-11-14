using System;
using System.Threading.Tasks;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native.Windows {
 public interface INativeInputSession:IDisposable {
  Task Completion{get;}
  bool Ready{get;}
  bool Connected{get;}
  bool ResourcesReleased{get;}
  string Error{get;}
  string CleanupError{get;}
  int ProcessId{get;}
  long CreationFileTime{get;}
  int[] HelperProcessIds{get;}
  OrderedInputBatch Latest{get;}
  InputAuthority ReadAuthority();
  void Pump();
  void SetEnabled(bool enabled);
  void Accept(long cursor);
  void Stop();
 }
}

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 // Host boundary for a local adapter or a future Manager -> Runtime proxy.
 // It contains no engine objects, scene names, UI types, sockets, or camera handles.
 public interface ICalibrationService {
  CalibrationSession Session{get;}
  ICalibrationStore Store{get;}
  CalibrationProfile Applied{get;}
  bool Fresh{get;} bool Compatible{get;} bool CanEdit{get;} bool CanSave{get;} bool Capturing{get;} bool Busy{get;}
  string Message{get;} string CameraId{get;} string CameraName{get;}
  event Action Changed;
  Task<JArray> EnumerateCameras();
  void ApplyCamera(string id,string name);
  void StartReference();
  void BindCurrentSource();
  void SelectPreset(CalibrationProfile profile);
  void NewPreset();
  bool Save(string name,bool saveAs,bool finish);
  void NotifyEdit();
  void Report(string message);
 }
}

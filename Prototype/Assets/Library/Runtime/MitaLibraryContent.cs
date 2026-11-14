using System.Linq;
using UnityEngine;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Mita;

// The library owns projection, tracking, audio pause and the native desktop surface.
public sealed class MitaLibraryContent : MonoBehaviour {
 MitaDesktop input;
 LibraryPreviewController library;
 NativeRenderInstance native;
 float refreshAt;
 void Start(){
  input=GetComponent<MitaDesktop>();native=FindObjectOfType<NativeRenderInstance>();library=FindObjectOfType<LibraryPreviewController>();
  var view=FindObjectOfType<CalibrationDemoView>();
  GetComponent<MitaBehaviour>().MapCursor=screen=>native||DesktopRuntime.IsRuntime?(Vector2?)screen:view&&view.TryMapArtworkCursor(screen,out var cursor)?(Vector2?)cursor:null;
 }
 void Update(){
  GetComponent<MitaInteractions>().Receive(native?native.CurrentRaw:library.Runtime.CurrentRaw);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
  if(native){if(Time.unscaledTime>=refreshAt){refreshAt=Time.unscaledTime+1;input.managedDisplay=native.DesktopTarget?DesktopPlatform.Displays().SingleOrDefault(x=>x.Id==native.DisplayId):null;}return;}
  if(!DesktopRuntime.IsRuntime||Time.unscaledTime<refreshAt)return;
  refreshAt=Time.unscaledTime+1;
  input.managedDisplay=DesktopPlatform.Displays().SingleOrDefault(x=>x.Id==library.TargetDisplayId);
#endif
 }
}

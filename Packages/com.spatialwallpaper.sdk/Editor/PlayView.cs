using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SpatialWallpaper.Sdk.Editor {
 [InitializeOnLoad]internal static class PlayView {
  static PlayView(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.quitting+=Stop;EditorApplication.playModeStateChanged+=value=>{if(value==PlayModeStateChange.EnteredEditMode)Stop();};}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Start(){
   Stop();if(Application.platform!=RuntimePlatform.WindowsEditor||UnityEngine.Object.FindObjectsOfType<SceneDefinition>().Length==0)return;
   var views=UnityEngine.Object.FindObjectsOfType<WindowProjection>().Where(value=>value.enabled&&value.GetComponent<Camera>().enabled&&value.GetComponent<Camera>().cameraType==CameraType.Game).ToArray();
   if(views.Length!=1)throw new InvalidOperationException("play-view-requires-one-window-camera");
   View.BindPlay(views[0]);
  }
  static void Stop(){View.BindPlay(null);}
 }
}

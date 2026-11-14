using System;using System.Linq;using UnityEditor;using UnityEngine;using UnityEngine.Rendering.Universal;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Editor {
 [InitializeOnLoad]internal static class PlayPresentation {
  static UnityPresentationState state;static GameObject owner;static UniversalAdditionalCameraData cameraData;static bool previousPost;
  static PlayPresentation(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.quitting+=Stop;EditorApplication.playModeStateChanged+=value=>{if(value==PlayModeStateChange.EnteredEditMode)Stop();};}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Start(){
   Stop();if(Application.platform!=RuntimePlatform.WindowsEditor)return;
   var definitions=UnityEngine.Object.FindObjectsOfType<SceneDefinition>().Where(value=>value.contentRoot&&value.contentRoot.GetComponent<ScenePresentationSettings>() is ScenePresentationSettings settings&&settings.applyInPlay&&settings.enabled).ToArray();
   if(definitions.Length==0)return;if(definitions.Length!=1)throw new InvalidOperationException("play-presentation-requires-one-scene-definition");
   var definition=definitions[0];var settings=definition.contentRoot.GetComponent<ScenePresentationSettings>();var config=ScriptSceneExporter.EncodePresentation(settings);
   var cameras=UnityEngine.Object.FindObjectsOfType<WindowProjection>().Select(value=>value.GetComponent<Camera>()).Where(value=>value&&value.enabled&&value.cameraType==CameraType.Game).ToArray();
   if(cameras.Length!=1)throw new InvalidOperationException("play-presentation-requires-one-window-camera");
   try{owner=new GameObject("SDK Play presentation"){hideFlags=HideFlags.HideAndDontSave};owner.transform.SetParent(definition.contentRoot,false);cameraData=cameras[0].GetComponent<UniversalAdditionalCameraData>();if(!cameraData)throw new InvalidOperationException("play-presentation-requires-URP-camera");previousPost=cameraData.renderPostProcessing;cameraData.renderPostProcessing=true;state=new UnityPresentationState(config,owner);state.Configure(cameras[0]);}
   catch{Stop();throw;}
  }
  static void Stop(){state?.Dispose();state=null;if(cameraData)cameraData.renderPostProcessing=previousPost;cameraData=null;if(owner)UnityEngine.Object.DestroyImmediate(owner);owner=null;}
 }
}

using System.Linq;
using UnityEngine;
using UnityEditor;
using SpatialWallpaper.Sdk.Native.Unity;

namespace SpatialWallpaper.Sdk.Editor {
 [DefaultExecutionOrder(-10000)]internal sealed class PlaySceneQueryPump:MonoBehaviour {
  internal NativePlayQueries Session;
  void Update(){Session.Pump();}
 }
 [InitializeOnLoad]internal static class PlaySceneQueries {
  static NativePlayQueries session;static GameObject pump;
  static PlaySceneQueries(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.quitting+=Stop;EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)Stop();};}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Start(){
   Stop();var roots=UnityEngine.Object.FindObjectsOfType<SceneDefinition>().Select(value=>value.transform).ToArray();if(roots.Length==0)return;session=new NativePlayQueries(roots);SceneQueries.BindPlay(session.Call);pump=new GameObject("SDK native query frames"){hideFlags=HideFlags.HideAndDontSave};UnityEngine.Object.DontDestroyOnLoad(pump);pump.AddComponent<PlaySceneQueryPump>().Session=session;
  }
  static void Stop(){SceneQueries.BindPlay(null);if(pump)UnityEngine.Object.DestroyImmediate(pump);pump=null;session?.Dispose();session=null;}
 }
}

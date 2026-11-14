using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpatialWallpaper.Desktop {
 // The same product Player has one minimal first scene. Renderer roles never
 // instantiate the Library's manager/calibration/Tracker/tray/store components.
 public sealed class NativeRenderEntry:MonoBehaviour {
  public const string ScenePath="Assets/Desktop/Scenes/NativeEntry.unity";
  public const string LibraryScene="Assets/Library/Scenes/Library.unity";
  IEnumerator Start(){
   DontDestroyOnLoad(gameObject);
   string path=CalibrationRuntime.Argument("--native-render-session");
   if(!string.IsNullOrEmpty(path)){gameObject.AddComponent<NativeRenderInstance>().Initialize(path);Destroy(this);yield break;}
   yield return SceneManager.LoadSceneAsync(LibraryScene,LoadSceneMode.Single);
   // Prepared original assets need not have the development driver serialized.
   // Attach it only for the explicit test launch, never in a renderer role.
   if(!DesktopRuntime.IsRuntime&&(!string.IsNullOrEmpty(CalibrationRuntime.Argument("--main-manager-smoke"))||!string.IsNullOrEmpty(CalibrationRuntime.Argument("--main-manager-probe")))&&!FindObjectOfType<MainManagerSmoke>())FindObjectOfType<DesktopManager>().gameObject.AddComponent<MainManagerSmoke>();
   Destroy(gameObject);
  }
 }
}

using System.Collections;
using UnityEngine;
using SpatialWallpaper.Content;
public sealed class RuntimeSeriesObserver:MonoBehaviour {
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(NaturalSeriesCapture.Argument("--lifecycle-output")!=null)new GameObject("Lifecycle validation observer").AddComponent<ContentLifecycleObserver>();if(NaturalSeriesCapture.Argument("--series-output")!=null)new GameObject("Runtime validation observer").AddComponent<RuntimeSeriesObserver>();}
 IEnumerator Start(){float deadline=Time.realtimeSinceStartup+60;RuntimeContentPresentation presentation=null;while(Time.realtimeSinceStartup<deadline){presentation=FindObjectOfType<RuntimeContentPresentation>();if(presentation&&presentation.Ready)break;yield return null;}if(!presentation||!presentation.Ready){Application.Quit(1);yield break;}NaturalSeriesCapture.Launch(presentation.DisplayCamera);}
}

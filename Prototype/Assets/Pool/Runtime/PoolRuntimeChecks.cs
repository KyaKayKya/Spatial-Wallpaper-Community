using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Opt-in Player checks; absent from the ordinary scene lifecycle and performance path.
public sealed class PoolRuntimeChecks:MonoBehaviour {
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--pool-checks")>=0)new GameObject("PoolRuntimeChecks").AddComponent<PoolRuntimeChecks>();}
 IEnumerator Start(){
  var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"--pool-checks")+1];Directory.CreateDirectory(output);
  yield return new WaitForSeconds(2);
  var state=FindObjectOfType<PoolMaterialState>();var animation=FindObjectOfType<PoolAnimation>();
  var ring=animation.ring.GetComponent<Renderer>();float lo=float.MaxValue,hi=float.MinValue;
  float end=Time.time+7;bool contact=true;
  while(Time.time<end){
   yield return new WaitForEndOfFrame();
   lo=Mathf.Min(lo,animation.ring.position.y);hi=Mathf.Max(hi,animation.ring.position.y);
   contact&=ring.bounds.min.y<state.waterSurface.position.y&&ring.bounds.max.y>state.waterSurface.position.y;
  }
  bool scaleCorrect=true;var original=state.transform.localScale;
  foreach(float factor in new[]{2f,1f}){
   state.transform.localScale=original*factor;yield return new WaitForEndOfFrame();
   foreach(var r in state.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)if(m.HasProperty("_WaterLevel")){
    scaleCorrect&=Mathf.Abs(m.GetFloat("_WaterLevel")-state.waterSurface.position.y)<.00001f;
    scaleCorrect&=Mathf.Abs(m.GetFloat("_WorldScale")-state.waterSurface.lossyScale.x)<.00001f;
   }
   scaleCorrect&=Mathf.Abs(animation.water.GetFloat("_DepthEnd")-.12f*state.waterSurface.lossyScale.x/(.53f/11.2f))<.00001f;
  }
  bool pass=contact&&scaleCorrect&&hi-lo>.003f;
  File.WriteAllText(Path.Combine(output,"runtime-checks.txt"),$"{(pass?"PASS":"FAIL")} ring_vertical_range_m={hi-lo:F6} ring_bounds_cross_water={contact} live_water_height_and_scale={scaleCorrect}");
  Application.Quit(pass?0:1);
 }
}

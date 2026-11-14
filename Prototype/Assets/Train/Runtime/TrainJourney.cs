using UnityEngine;
namespace SpatialWallpaper.Train {
 // Content-only clock: no camera, eye position, tracker or global shader state.
 public sealed class TrainJourney : MonoBehaviour {
  public Renderer[] windows;
  public Transform furnishings;
  public bool externalClock;
  public bool animate = true;
  MaterialPropertyBlock block;
  Vector3 rest;
  void Awake(){block=new MaterialPropertyBlock();if(furnishings)rest=furnishings.localPosition;}
  void OnEnable(){if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--train-static")>=0)animate=false;}
  void Update(){if(!externalClock)Evaluate(animate?Time.time:0);}
  public void Evaluate(float time){
   if(block==null)block=new MaterialPropertyBlock();
   foreach(var r in windows){if(!r)continue;r.GetPropertyBlock(block);block.SetFloat("_JourneyTime",time);r.SetPropertyBlock(block);}
   // The observer rides with the cabin. Suspension is expressed outside the glass.
   // Keep every interior transform rigid, including the upholstery and curtains.
   if(furnishings)furnishings.localPosition=rest;
  }
  void OnDisable(){if(furnishings)furnishings.localPosition=rest;}
 }
}

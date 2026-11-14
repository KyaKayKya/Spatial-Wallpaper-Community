using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaEffects:MonoBehaviour {
  public ParticleSystem click,wink;
  public Camera view;
  public WindowProjection projection;
  public AudioSource clickAudio;
  public int Clicks{get;private set;}
  public int Winks{get;private set;}
  public void Click(Vector2 pixel){
   var frame=projection.screenFrame;
   var plane=new Plane(frame?frame.forward:Vector3.forward,frame?frame.position:Vector3.zero);
   var ray=view.ScreenPointToRay(pixel);plane.Raycast(ray,out float distance);
   click.Stop(true,ParticleSystemStopBehavior.StopEmitting);
   click.transform.position=ray.GetPoint(distance);
   click.transform.rotation=frame?frame.rotation:Quaternion.identity;
   click.transform.localScale=Vector3.one*(50*projection.displayHeight/view.pixelHeight);
   click.Play(true);clickAudio.PlayOneShot(clickAudio.clip);Clicks++;
  }
  public void Wink(){wink.Play(true);Winks++;}
  void Start(){click.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);wink.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
 }
}

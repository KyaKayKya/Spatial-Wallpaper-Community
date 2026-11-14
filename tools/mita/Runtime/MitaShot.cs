using UnityEngine;

namespace SpatialWallpaper.Mita {
 // Sample the original camera track and character performance on the same clock.
 // The physical window camera remains exclusively eye-driven.
 [DefaultExecutionOrder(50)]
 public sealed class MitaShot : MonoBehaviour {
  public AnimationClip originalOpening;
  public Camera sampledCamera;
  public Light sampledLight;
  public Light key;
  public Transform placement;
  public Camera windowCamera;
  public Renderer floor;
  public Renderer backdrop;
  public Renderer[] characterSurfaces;
  public float lightingGain=.63f;
  public AnimationCurve[] cameraPosition,cameraEuler;
  public Vector3 cameraOffsetPosition,cameraOffsetEuler;
  public float sourceFieldOfView;
  public AnimationCurve exposure;
  public Vector3 SourceCameraPosition{get;private set;}
  public Quaternion SourceCameraRotation{get;private set;}
  MaterialPropertyBlock outline;
  public float Clock{get;private set;}
  public void Restart(){Clock=0;var animator=sampledCamera.GetComponent<Animator>();animator.Rebind();animator.Update(0);}
  public void Sample(float time){
   if(!Application.isPlaying)originalOpening.SampleAnimation(sampledCamera.gameObject,Mathf.Min(time,originalOpening.length));
   Quaternion offset=Quaternion.Euler(cameraOffsetEuler);
   SourceCameraPosition=cameraOffsetPosition+offset*new Vector3(cameraPosition[0].Evaluate(time),cameraPosition[1].Evaluate(time),cameraPosition[2].Evaluate(time));
   SourceCameraRotation=offset*Quaternion.Euler(cameraEuler[0].Evaluate(time),cameraEuler[1].Evaluate(time),cameraEuler[2].Evaluate(time));
   // Match the original lens at the face plane while preserving uniform geometry
   // and the physical off-axis window. Depth perspective differs from the source lens.
   float lensRatio=.6f*Mathf.Tan(sourceFieldOfView*.5f*Mathf.Deg2Rad)/(.298125f*.5f);
   Vector3 focus=new Vector3(0,1.6f,.19f);
   Vector3 virtualCamera=focus+(SourceCameraPosition-focus)*lensRatio;
   Quaternion inverse=Quaternion.Inverse(SourceCameraRotation);
   float scale=.298125f/.3f*(.7f/.6f);
   placement.localPosition=new Vector3(0,0,-.6f)+inverse*(-virtualCamera)*scale;
   placement.localRotation=inverse;
   placement.localScale=Vector3.one*scale;
   key.transform.position=placement.TransformPoint(new Vector3(0,1.734f,.35f));
   float worldScale=scale*placement.parent.lossyScale.x;
   key.intensity=2*lightingGain*Mathf.Pow(2,exposure.Evaluate(time));key.range=sampledLight.range*worldScale;key.shadowNearPlane=sampledLight.shadowNearPlane*worldScale;
   windowCamera.backgroundColor=sampledCamera.backgroundColor*.3f;
   floor.sharedMaterial.color=windowCamera.backgroundColor;
   if(backdrop)backdrop.sharedMaterial.color=new Color(.24f,.11f,.3f,.12f*Mathf.SmoothStep(0,1,(time-1)/2.5f));
   if(outline==null)outline=new MaterialPropertyBlock();
   outline.SetColor("_LineColor",Color.Lerp(Color.black,new Color(.6886792f,.21115166f,.55106676f),Mathf.SmoothStep(0,1,time-1)));
   foreach(var surface in characterSurfaces)surface.SetPropertyBlock(outline);
  }
  void LateUpdate(){if(Time.timeScale!=0)Clock+=Time.deltaTime;Sample(Clock);}
 }
}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaGestureAudit:MonoBehaviour {
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--gestures")<0)yield break;
   yield return new WaitForSeconds(8);
   var gestures=GetComponent<MitaGestures>();var bones=gestures.actor.GetComponentsInChildren<Transform>();var left=bones.Single(t=>t.name=="Left wrist");var right=bones.Single(t=>t.name=="Right wrist");
   var face=gestures.actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(m=>m.name=="Head");int blink=face.sharedMesh.GetBlendShapeIndex("Blink");
   string output=Path.Combine(Environment.GetEnvironmentVariable("MITA_OUTPUT"),"gestures");Directory.CreateDirectory(output);
   using(var report=new StreamWriter(Path.Combine(output,"audit.csv"))){
    report.WriteLine("name,seconds,leftTravel,rightTravel,returnedIdle,maxExtraFaceLayer,idleBlink,minGestureSpeed,maxGestureSpeed");
    for(int index=0;index<gestures.names.Length;index++){
     gestures.actor.SetTrigger("Blink");yield return new WaitForSeconds(.12f);
     Vector3 l=gestures.actor.transform.InverseTransformPoint(left.position),r=gestures.actor.transform.InverseTransformPoint(right.position);float lm=0,rm=0,time=0,faceLayer=0,minSpeed=float.PositiveInfinity,maxSpeed=0;
     gestures.Play(index);int image=0;
     while(time<gestures.durations[index]+.8f){
      yield return new WaitForEndOfFrame();time+=Time.deltaTime;
      if(gestures.Busy)faceLayer=Mathf.Max(faceLayer,gestures.actor.GetLayerWeight(1));
      var state=gestures.actor.GetCurrentAnimatorStateInfo(0);if(state.IsTag("Gesture")&&!gestures.actor.IsInTransition(0)){float speed=state.speed*state.speedMultiplier*gestures.actor.speed;minSpeed=Mathf.Min(minSpeed,speed);maxSpeed=Mathf.Max(maxSpeed,speed);}
      lm=Mathf.Max(lm,Vector3.Distance(l,gestures.actor.transform.InverseTransformPoint(left.position)));rm=Mathf.Max(rm,Vector3.Distance(r,gestures.actor.transform.InverseTransformPoint(right.position)));
      if(image<4&&time>(image*.25f+.12f)*gestures.durations[index]){ScreenCapture.CaptureScreenshot(Path.Combine(output,index+"-"+image+".png"));image++;}
     }
     report.WriteLine(FormattableString.Invariant($"{gestures.names[index]},{time},{lm},{rm},{gestures.actor.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.MitaIdle")},{faceLayer},{face.GetBlendShapeWeight(blink)},{minSpeed},{maxSpeed}"));report.Flush();
    }
   }
   Application.Quit();
  }
 }
}

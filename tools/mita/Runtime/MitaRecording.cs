using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaRecording : MonoBehaviour {
  [Serializable] sealed class AudioEvents {public List<float> clicks=new List<float>();}
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--record")<0)yield break;
   Time.captureDeltaTime=1f/30;
   var behaviour=GetComponent<MitaBehaviour>();
   var look=GetComponent<MitaLook>();
   bool demo=Array.IndexOf(Environment.GetCommandLineArgs(),"--demo")>=0;
   bool interactions=Array.IndexOf(Environment.GetCommandLineArgs(),"--interactions")>=0;look.enabled=interactions;look.automatic=interactions;
   var audio=new AudioEvents();
   string suffix=interactions?"-interactions":demo?"-parallax":"";
   string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");string frames=Path.Combine(output,"frames"+suffix);Directory.CreateDirectory(frames);
   using(var evidence=new StreamWriter(Path.Combine(output,"recording"+suffix+".csv"))){
    evidence.WriteLine("frame,time,eyeX,eyeY,eyeZ,cameraAngle,cornerErrorPx,placementX,placementY,placementZ,placementYaw,faceYaw");
    var shot=GetComponent<MitaShot>();
    var head=behaviour.actor.GetComponentsInChildren<Transform>().Single(t=>t.name=="Head"&&!t.GetComponent<Renderer>());
    for(int frame=0;frame<480;frame++){
     if(interactions&&(frame==225||frame==300||frame==306)){
      Vector2 face=behaviour.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
      if(behaviour.Touch(face))audio.clicks.Add(shot.Clock);
     }
     yield return new WaitForEndOfFrame();
     ScreenCapture.CaptureScreenshot(Path.Combine(frames,frame.ToString("D5")+".png"));
     float error=0;
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      Vector3 point=behaviour.view.WorldToViewportPoint(behaviour.volume.FrontCorner(x,y));
      error=Mathf.Max(error,new Vector2((point.x-x-.5f)*Screen.width,(point.y-y-.5f)*Screen.height).magnitude);
     }
     Vector3 eye=behaviour.projection.EyeScreen;
     var position=behaviour.placement.localPosition;
     var facing=behaviour.placement.InverseTransformDirection((look.leftEye.position+look.rightEye.position)*.5f-head.position);
     float faceYaw=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
     evidence.WriteLine(FormattableString.Invariant($"{frame},{shot.Clock},{eye.x},{eye.y},{eye.z},{Quaternion.Angle(behaviour.view.transform.rotation,Quaternion.identity)},{error},{position.x},{position.y},{position.z},{behaviour.placement.localEulerAngles.y},{faceYaw}"));
    }
   }
   if(interactions)File.WriteAllText(Path.Combine(output,"interaction-audio.json"),JsonUtility.ToJson(audio,true));
   yield return null;yield return null;Time.captureDeltaTime=0;Application.Quit();
  }
 }
}

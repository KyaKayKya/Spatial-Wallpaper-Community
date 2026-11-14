using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(65)]
 public sealed class MitaGazeAudit:MonoBehaviour {
  MitaBehaviour behaviour;MitaLook look;MitaShot shot;
  Vector2 heldCursor;bool clicked,performed,fallbackClicked,paused,active;
  bool fresh;
  float activationStep;
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--gaze")<0)yield break;
   behaviour=GetComponent<MitaBehaviour>();look=GetComponent<MitaLook>();shot=GetComponent<MitaShot>();
   look.enabled=false;look.ResetGaze();active=true;Time.captureDeltaTime=1f/30;
   string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");string frames=Path.Combine(output,"frames-gaze");Directory.CreateDirectory(frames);
   int frame=0;
   using(var writer=new StreamWriter(Path.Combine(output,"gaze.csv"))){
    writer.WriteLine("frame,time,source,fresh,near,busy,targetX,targetY,targetZ,viewX,viewY,viewZ,eyesZ,eyeWeight,headWeight,eyeError,cameraAngle,cornerError,activationStep");
    while(shot.Clock<28){
     yield return new WaitForEndOfFrame();
     ScreenCapture.CaptureScreenshot(Path.Combine(frames,frame.ToString("D5")+".png"));
     var target=look.RequestedTarget;var view=behaviour.view.transform.position;
     float error=Mathf.Max(Vector3.Angle(look.leftEye.TransformDirection(look.leftAim.aimVector),look.eyesTarget.position-look.leftEye.position),Vector3.Angle(look.rightEye.TransformDirection(look.rightAim.aimVector),look.eyesTarget.position-look.rightEye.position));
     float corners=0;
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      var point=behaviour.view.WorldToViewportPoint(behaviour.volume.FrontCorner(x,y));
      corners=Mathf.Max(corners,new Vector2((point.x-x-.5f)*Screen.width,(point.y-y-.5f)*Screen.height).magnitude);
     }
     writer.WriteLine(FormattableString.Invariant($"{frame},{shot.Clock},{look.Source},{fresh},{look.Nearby},{GetComponent<MitaGestures>().Busy},{target.x},{target.y},{target.z},{view.x},{view.y},{view.z},{(look.leftEye.position.z+look.rightEye.position.z)*.5f},{look.leftAim.weight},{look.headAim.weight},{error},{Quaternion.Angle(behaviour.view.transform.rotation,Quaternion.identity)},{corners},{activationStep}"));frame++;
     if(shot.Clock>25&&!paused){
      paused=true;float before=Time.time;var source=look.Source;float weight=look.leftAim.weight;
      behaviour.TogglePause();yield return new WaitForSecondsRealtime(.3f);
      bool frozen=Time.time==before&&look.Source==source&&look.leftAim.weight==weight;
      behaviour.TogglePause();File.WriteAllText(Path.Combine(output,"gaze-pause.json"),"{\"frozen\":"+(frozen?"true":"false")+"}");
     }
    }
   }
   yield return null;yield return null;Time.captureDeltaTime=0;Application.Quit();
  }
  void Update(){
   if(!active||Time.timeScale==0)return;
   float t=shot.Clock;
   fresh=(t>=7&&t<13.5f)||(t>=14&&t<15);
   if(fresh)behaviour.projection.SetEye(new Vector3(Mathf.Sin(t*.6f)*.08f,Mathf.Sin(t*.35f)*.025f,-.6f));
   Vector2 cursor=new Vector2(-1000,-1000);
   if(t>=8.8f&&t<11.8f){
    cursor=behaviour.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
    cursor+=new Vector2(Mathf.Sin(t*3)*Screen.width*.045f,Mathf.Cos(t*2)*Screen.height*.04f);heldCursor=cursor;
   }else if(t>=11.8f&&t<13.5f)cursor=heldCursor;
   if(t>=9.5f&&!clicked){look.Glance();clicked=true;}
   if(t>=16&&!performed){look.Glance();GetComponent<MitaGestures>().Play(2);performed=true;}
   if(t>=23&&!fallbackClicked){look.Glance();fallbackClicked=true;}
   var previous=look.Source;bool released=look.leftAim.weight==0;
   var nativeDirection=look.leftEye.TransformDirection(look.leftAim.aimVector);
   look.UpdateGaze(Time.time,Time.deltaTime,cursor,fresh,GetComponent<MitaGestures>().Busy);
   activationStep=previous==MitaAttentionSource.Native&&released&&look.Source!=MitaAttentionSource.Native?
    Vector3.Angle(nativeDirection,look.eyesTarget.position-(look.leftEye.position+look.rightEye.position)*.5f):0;
  }
 }
}

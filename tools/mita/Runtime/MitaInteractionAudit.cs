using System;
using System.IO;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialTracking;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(-60)]
 public sealed class MitaInteractionAudit:MonoBehaviour {
  bool active;string output;MitaInteractions interactions;MitaLook look;MitaBehaviour behaviour;StreamWriter csv;int checkpoint;
  float maxHeadTurn;Quaternion reference;bool referenceSet,petSeen,pauseChecked,pausedSample;float pauseAt;Quaternion pausedHead;
  int observedStarts,observedCompletes;float responseAt,expectedDuration;
  static void Require(bool value,string message){if(!value){Application.Quit(2);throw new InvalidOperationException("Interaction audit: "+message);}}
  void Start(){
   active=Array.IndexOf(Environment.GetCommandLineArgs(),"--interactions-v2")>=0;if(!active)return;
   Time.captureFramerate=30;output=Environment.GetEnvironmentVariable("MITA_OUTPUT");Directory.CreateDirectory(output);
   interactions=GetComponent<MitaInteractions>();look=GetComponent<MitaLook>();behaviour=GetComponent<MitaBehaviour>();
   File.Delete(Path.Combine(output,"interactions-v2.json"));
   csv=new StreamWriter(Path.Combine(output,"interactions-v2.csv"));csv.AutoFlush=true;csv.WriteLine("time,accepted,started,completed,pending,current,head_weight,head_x,head_y,head_z,layer_weight,layer_time");
  }
  static float Nod(float t,float start){float u=(t-start)/.5f;return u<0||u>1?0:14*Mathf.Sin(u*Mathf.PI);}
  static float Shake(float t,float start){float u=(t-start)/.85f;return u<0||u>1?0:17*Mathf.Sin(u*Mathf.PI*2);}
  void Update(){
   if(!active)return;float t=Time.time;
   if(!referenceSet&&t>=7){reference=look.head.localRotation;referenceSet=true;}
   if(t>=9&&t<10&&!pauseChecked){
    if(Time.timeScale!=0){Require(interactions.HeadActive,"pause during a head response");pauseAt=Time.realtimeSinceStartup;Time.timeScale=0;}
    else if(Time.realtimeSinceStartup-pauseAt>.3f){Require(Quaternion.Angle(pausedHead,look.head.localRotation)<.1f,"pause preserves animated head pose");Time.timeScale=1;pauseChecked=true;}
   }
   float pitch=Nod(t,8)+Nod(t,14)+Nod(t,14.6f)+Nod(t,15.2f)+Nod(t,15.8f)+Nod(t,21)+Nod(t,32)+Nod(t,32.6f);
   float yaw=Shake(t,23)+Shake(t,24.5f);
   var q=Quaternion.Euler(pitch,yaw,0);
   bool lost=t>33.15f&&t<34||t>=58&&t<69;
   interactions.Receive(lost?null:new RawTrackingSample("audit-head",(ulong)Time.frameCount+1,QpcClock.Now,new[]{0.0,0,.6},new[]{(double)q.x,q.y,q.z,q.w},new JObject(),headPoint:new[]{0.0,0,.6}));
   if(t>=11&&checkpoint==0){Require(interactions.Accepted==1&&interactions.Completed==1&&pauseChecked,"one nod must complete after pause");checkpoint++;}
   if(t>=22&&checkpoint==1){Require(interactions.Accepted==4&&interactions.Completed==4,"three accepted nods, fourth and cooldown inputs rejected");checkpoint++;}
   if(t>=29.5f&&checkpoint==2){Require(interactions.Accepted==6&&interactions.Completed==6,"two shakes must produce two complete responses");checkpoint++;}
   if(t>=34.6f&&checkpoint==3){Require(interactions.Accepted==8&&interactions.Started==7&&interactions.Completed==6&&interactions.Pending==0&&!interactions.HeadActive,"loss must cancel active and queued responses");checkpoint++;}
   if(t>=35.1f&&checkpoint==4){Require(behaviour.Touch(AccessoryPoint(-1.05f,.55f)),"hairpin click");checkpoint++;}
   if(t>=43.1f&&checkpoint==5){Require(!GetComponent<MitaGestures>().Busy,"peace returns to idle");Require(behaviour.Touch(AccessoryPoint(1,-1.15f)),"bow click");checkpoint++;}
   if(t>=51&&checkpoint==6){Require(!GetComponent<MitaGestures>().Busy,"welcome returns to idle");checkpoint++;}
   if(t>=53&&t<54){
    var center=behaviour.view.WorldToScreenPoint(look.head.position);float radius=Mathf.Abs(behaviour.view.WorldToScreenPoint(look.head.position+behaviour.view.transform.right*.13f*behaviour.placement.lossyScale.x).x-center.x);
    interactions.MoveCursor(new Vector2(center.x+Mathf.Sin((t-53)*16)*radius*.6f,center.y+radius),false);
   }
   if(t>=71&&checkpoint==7){Require(petSeen,"stroking must enter original pet facial state");Require(GetComponent<MitaGestures>().Busy,"return after absence must greet with original wave");checkpoint++;}
   if(t>=78&&checkpoint==8){Require(!GetComponent<MitaGestures>().Busy,"greeting returns to idle");Require(maxHeadTurn>6,"original head layer must visibly rotate head");csv.Flush();File.WriteAllText(Path.Combine(output,"interactions-v2.json"),JsonUtility.ToJson(new Result{passed=true,accepted=interactions.Accepted,started=interactions.Started,completed=interactions.Completed,maxHeadTurn=maxHeadTurn},true));Application.Quit();checkpoint++;}
  }
  Vector2 AccessoryPoint(float x,float y){var center=behaviour.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);float unit=Mathf.Abs(behaviour.view.WorldToScreenPoint(look.head.position+behaviour.view.transform.right*.1f*behaviour.placement.lossyScale.x).x-behaviour.view.WorldToScreenPoint(look.head.position).x);return (Vector2)center+new Vector2(x,y)*unit;}
  void LateUpdate(){
   if(!active)return;float t=Time.time;var e=look.head.localEulerAngles;
   if(interactions.Started!=observedStarts){observedStarts=interactions.Started;responseAt=t;expectedDuration=interactions.Current==MitaHeadGesture.Nod?1.8f:2.4f;}
   if(interactions.Completed!=observedCompletes){observedCompletes=interactions.Completed;Require(Mathf.Abs(t-responseAt-expectedDuration)<.08f,"head response must retain the relaxed full duration");}
   if(Time.timeScale==0&&!pausedSample){pausedHead=look.head.localRotation;pausedSample=true;}
   if(t>=53&&t<56&&interactions.actor.GetCurrentAnimatorStateInfo(1).IsName("Face.Pet"))petSeen=true;
   if(interactions.HeadActive){if(referenceSet)maxHeadTurn=Mathf.Max(maxHeadTurn,Quaternion.Angle(reference,look.head.localRotation));Require(look.headAim.weight==0,"eye tracking must not overwrite original head response");}
   csv.WriteLine(FormattableString.Invariant($"{t},{interactions.Accepted},{interactions.Started},{interactions.Completed},{interactions.Pending},{interactions.Current},{look.headAim.weight},{e.x},{e.y},{e.z},{interactions.actor.GetLayerWeight(interactions.headLayer)},{interactions.actor.GetCurrentAnimatorStateInfo(interactions.headLayer).normalizedTime}"));
   if(Time.frameCount%15==0&&(t>8.7f&&t<10||t>14.7f&&t<18||t>24.7f&&t<28||t>53&&t<55.5f))StartCoroutine(Shot(t));
   if(t>=34.9f&&t<34.94f)StartCoroutine(Shot(t));
  }
  void OnGUI(){if(!active||Time.time<34.8f||Time.time>=35.1f)return;foreach(var p in new[]{AccessoryPoint(-1.05f,.55f),AccessoryPoint(1,-1.15f)})GUI.DrawTexture(new Rect(p.x-4,Screen.height-p.y-4,8,8),Texture2D.whiteTexture);}
  IEnumerator Shot(float time){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"interaction-"+time.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
  [Serializable]sealed class Result{public bool passed;public int accepted,started,completed;public float maxHeadTurn;}
  void OnDestroy(){csv?.Dispose();}
 }
}

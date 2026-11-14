using UnityEngine;
using SpatialTracking;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(60)]
 public sealed class MitaInteractions:MonoBehaviour {
  public Animator actor;
  public int headLayer;
  public float nodDuration,shakeDuration;
  readonly MitaHeadGestureDetector detector=new MitaHeadGestureDetector();
  readonly MitaResponseQueue responses=new MitaResponseQueue();
  MitaBehaviour behaviour;MitaLook look;MitaGestures gestures;
  string stream;ulong frame;
  float lastRaw=-10,until,fadeUntil,petAt,awayAt=-1,backAt=-1;
  bool hadUser,returnGreeting;
  int strokeSign,strokes;float strokeX,strokeAt;
  public int Accepted{get;private set;}public int Started{get;private set;}public int Completed{get;private set;}
  public MitaHeadGesture Current{get;private set;}
  public bool HeadActive=>Current!=MitaHeadGesture.None||Time.time<fadeUntil;
  public int Pending=>responses.Pending;
  void Awake(){behaviour=GetComponent<MitaBehaviour>();look=GetComponent<MitaLook>();gestures=GetComponent<MitaGestures>();}
  public void Receive(RawTrackingSample sample){
   if(Time.timeScale==0){detector.Reset();responses.Reset();return;}
   var head=sample?.Head;
   if(sample==null||!sample.Fresh(QpcClock.Now)||head==null){
    if(hadUser&&awayAt<0)awayAt=Time.time;
    backAt=-1;returnGreeting=false;CancelHead();return;
   }
   lastRaw=Time.time;
   if(stream!=sample.Stream){CancelHead();stream=sample.Stream;frame=0;}
   if(frame==sample.Frame)return;frame=sample.Frame;
   if(!hadUser){hadUser=true;awayAt=-1;}
   if(awayAt>=0){if(backAt<0)backAt=Time.time;if(Time.time-backAt>=.8f){returnGreeting=backAt-awayAt>=10;awayAt=-1;}}
   if(gestures.Busy||GetComponent<MitaShot>().Clock<6.5f){detector.Reset();responses.Reset();return;}
   var angles=new Quaternion((float)head[0],(float)head[1],(float)head[2],(float)head[3]).eulerAngles;
   var gesture=detector.Observe(Time.time,angles.x,angles.y,true);
   if(gesture!=MitaHeadGesture.None&&responses.Accept(Time.time,gesture))Accepted++;
  }
  public void CancelHead(){
   detector.Reset();responses.Reset();Current=MitaHeadGesture.None;until=0;fadeUntil=0;
   if(actor&&headLayer<actor.layerCount)actor.SetLayerWeight(headLayer,0);
  }
  void Update(){
   if(Time.timeScale==0)return;
   if(gestures.Busy){CancelHead();return;}
   if(Time.time-lastRaw>.25f&&HeadActive)CancelHead();
   if(Current!=MitaHeadGesture.None&&Time.time>=until){
    actor.CrossFadeInFixedTime("HeadResponse.None",.15f,headLayer);Current=MitaHeadGesture.None;Completed++;responses.Complete(Time.time);fadeUntil=Time.time+.15f;
   }
   if(!HeadActive){
    var next=responses.StartNext(Time.time);
    if(next!=MitaHeadGesture.None){Current=next;Started++;until=Time.time+(next==MitaHeadGesture.Nod?nodDuration:shakeDuration);actor.SetLayerWeight(headLayer,1);actor.CrossFadeInFixedTime("HeadResponse."+next,.12f,headLayer);look.Glance();}
    else actor.SetLayerWeight(headLayer,0);
   }
   if(returnGreeting&&!HeadActive&&GetComponent<MitaShot>().Clock>=6.5f){returnGreeting=false;gestures.Play(3);return;}
   if(behaviour.InputEnabled&&GetComponent<MitaShot>().Clock>=6.5f)Stroke();
  }
  public bool Accessory(Vector2 cursor){
   if(gestures.Busy||HeadActive||Time.timeScale==0||GetComponent<MitaShot>().Clock<6.5f)return false;
   var center=behaviour.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
   float unit=Mathf.Abs(behaviour.view.WorldToScreenPoint(look.head.position+behaviour.view.transform.right*.1f*behaviour.placement.lossyScale.x).x-behaviour.view.WorldToScreenPoint(look.head.position).x);
   var local=(cursor-(Vector2)center)/unit;
   int gesture=(local-new Vector2(-1.05f,.55f)).sqrMagnitude<.22f*.22f?2:(local-new Vector2(1f,-1.15f)).sqrMagnitude<.4f*.4f?0:-1;
   if(gesture<0)return false;GetComponent<MitaEffects>().Click(cursor);gestures.Play(gesture);return true;
  }
  void Stroke(){
   Vector2 cursor;
   if(GetComponent<MitaDesktop>().TryGetCursor(out var desktop))cursor=new Vector2(desktop.x*Screen.width,desktop.y*Screen.height);
   else if(!behaviour.TryMapCursor(Input.mousePosition,out cursor))return;
   MoveCursor(cursor,Input.GetMouseButton(0));
  }
  public void MoveCursor(Vector2 cursor,bool pressed){
   if(gestures.Busy||HeadActive||Time.timeScale==0)return;
   var center=behaviour.view.WorldToScreenPoint(look.head.position);
   float radius=Mathf.Abs(behaviour.view.WorldToScreenPoint(look.head.position+behaviour.view.transform.right*.13f*behaviour.placement.lossyScale.x).x-center.x);
   bool nearby=behaviour.view.pixelRect.Contains(cursor)&&Mathf.Abs(cursor.x-center.x)<radius&&cursor.y>center.y+radius*.5f&&cursor.y<center.y+radius*1.8f;
   if(!nearby||pressed||Time.time<petAt){strokes=strokeSign=0;strokeX=cursor.x;return;}
   if(Time.time-strokeAt>1.2f)strokes=strokeSign=0;
   float movement=cursor.x-strokeX;if(Mathf.Abs(movement)<radius*.12f)return;
   int sign=movement>0?1:-1;strokeX=cursor.x;
   if(strokeSign==0)strokeAt=Time.time;
   else if(sign!=strokeSign){strokes++;strokeAt=Time.time;}
   strokeSign=sign;
   if(strokes<2)return;actor.ResetTrigger("Blink");actor.SetTrigger("Pet");look.Glance();petAt=Time.time+6;strokes=0;
  }
 }
}

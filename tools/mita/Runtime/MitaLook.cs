using UnityEngine;
using UnityEngine.Animations;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(70)]
 public sealed class MitaLook : MonoBehaviour {
  public Transform head,leftEye,rightEye,placement,headTarget,eyesTarget;
  public AimConstraint headAim,leftAim,rightAim;
  public MitaShot shot;
  public bool automatic;
  public Vector3 neutralFacing=Vector3.forward;
  readonly MitaAttention attention=new MitaAttention(EnvironmentSeed());
  Vector3 smoothEyes,smoothHead;
  Vector2 cursorAnchor;
  float headAfter;
  bool sampledCursor;
  public MitaAttentionSource Source=>attention.Source;
  public Vector3 RequestedTarget {get;private set;}
  public bool Nearby {get;private set;}
  public bool TrackingFresh {get;private set;}
  static int EnvironmentSeed()=>System.Environment.TickCount;
  public void ResetGaze(){
   attention.Reset(Time.time);sampledCursor=false;
   smoothEyes=leftEye.TransformDirection(leftAim.aimVector);smoothHead=head.TransformDirection(headAim.aimVector);
   headAim.weight=leftAim.weight=rightAim.weight=0;headAfter=Time.time+.15f;
  }
  public void Glance(){attention.Click(Time.time);}
  void Start(){ResetGaze();}
  void Update(){
   Vector3 cursor=Input.mousePosition;
   if(GetComponent<MitaDesktop>().TryGetCursor(out var desktopCursor))cursor=new Vector3(desktopCursor.x*Screen.width,desktopCursor.y*Screen.height,0);
   else cursor=GetComponent<MitaBehaviour>().TryMapCursor(cursor,out var mapped)?(Vector3)mapped:new Vector3(-100000,-100000,0);
   if(automatic){
    var view=GetComponent<MitaBehaviour>().view;
    var center=view.WorldToScreenPoint((leftEye.position+rightEye.position)*.5f);
    cursor=center+new Vector3(Mathf.Sin(Time.time*.45f)*Screen.width*.06f,Mathf.Sin(Time.time*.31f)*Screen.height*.06f,0);
   }
   UpdateGaze(Time.time,Time.deltaTime,cursor,GetComponent<MitaBehaviour>().projection.TrackingFresh,GetComponent<MitaGestures>().Busy);
  }
  public void UpdateGaze(float now,float delta,Vector2 cursor,bool fresh,bool busy){
   if(delta<=0)return;
   var behaviour=GetComponent<MitaBehaviour>();var view=behaviour.view;
   Vector2 normalized=new Vector2(cursor.x/Screen.width,cursor.y/Screen.height);
   bool moved=sampledCursor&&(normalized-cursorAnchor).sqrMagnitude>.0025f*.0025f;
   if(!sampledCursor||moved)cursorAnchor=normalized;
   sampledCursor=true;
   Vector3 center=view.WorldToScreenPoint(head.position);
   float radius=Mathf.Abs(view.WorldToScreenPoint(head.position+view.transform.right*.23f*placement.lossyScale.x).x-center.x);
   Nearby=center.z>0&&view.pixelRect.Contains(cursor)&&
    Mathf.Abs(cursor.x-center.x)<radius&&cursor.y>center.y-radius*1.8f&&cursor.y<center.y+radius;
   TrackingFresh=fresh;
   var previous=Source;
   var source=attention.Step(now,shot.Clock>=6.5f,busy,fresh,Nearby,moved||automatic);
   if(busy||shot.Clock<6.5f){headAim.weight=leftAim.weight=rightAim.weight=0;return;}
   if(source!=previous)headAfter=now+.15f;
   bool active=source!=MitaAttentionSource.Native;
   if(active){
    if(source==MitaAttentionSource.Mouse){
     var frame=behaviour.projection.screenFrame;
     var plane=new Plane(frame?frame.forward:Vector3.forward,frame?frame.position:Vector3.zero);
     var ray=view.ScreenPointToRay(cursor);plane.Raycast(ray,out var distance);RequestedTarget=ray.GetPoint(distance);
    }else{
     var projection=behaviour.projection;
     RequestedTarget=fresh?view.transform.position:(projection.screenFrame?projection.screenFrame.TransformPoint(projection.referenceEye):projection.referenceEye);
    }
   }
   var neutral=placement.rotation*Quaternion.LookRotation(neutralFacing,Vector3.up);
   if(active){
    if(previous==MitaAttentionSource.Native&&leftAim.weight==0){
     smoothEyes=leftEye.TransformDirection(leftAim.aimVector);smoothHead=head.TransformDirection(headAim.aimVector);
    }
    smoothEyes=Vector3.Slerp(smoothEyes,(RequestedTarget-(leftEye.position+rightEye.position)*.5f).normalized,1-Mathf.Exp(-delta/ .18f));
    if(now>=headAfter)smoothHead=Vector3.Slerp(smoothHead,(RequestedTarget-head.position).normalized,1-Mathf.Exp(-delta/.55f));
   }
   headTarget.position=LimitedTarget(head.position,neutral,head.position+smoothHead,25,14);
   var headFrame=Quaternion.LookRotation(head.TransformDirection(headAim.aimVector),head.TransformDirection(headAim.upVector));
   var eyesCenter=(leftEye.position+rightEye.position)*.5f;
   eyesTarget.position=LimitedTarget(eyesCenter,headFrame,eyesCenter+smoothEyes,28,16);
   float eyeWeight=active?1:0;
   leftAim.weight=rightAim.weight=Mathf.MoveTowards(leftAim.weight,eyeWeight,delta/(active?.2f:.5f));
   float turn=Vector3.Angle(neutral*Vector3.forward,smoothHead);
   bool responding=GetComponent<MitaInteractions>().HeadActive;
   headAim.constraintActive=!responding;
   float headWeight=active&&now>=headAfter&&!responding?.35f*Mathf.Clamp01((turn-8)/18):0;
   headAim.weight=responding?0:Mathf.MoveTowards(headAim.weight,headWeight,delta/.55f);
  }
  static Vector3 LimitedTarget(Vector3 origin,Quaternion frame,Vector3 target,float yawLimit,float pitchLimit){
   var local=(Quaternion.Inverse(frame)*(target-origin)).normalized;
   float yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-yawLimit,yawLimit);
   float pitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-pitchLimit,pitchLimit);
   return origin+frame*(Quaternion.Euler(pitch,yaw,0)*Vector3.forward);
  }
 }
}

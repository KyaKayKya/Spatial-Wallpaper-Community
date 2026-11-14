using UnityEngine;
// Screen coordinates in metres: X right, Y up, Z into the cabinet. The eye has Z < 0.
[DefaultExecutionOrder(100),RequireComponent(typeof(Camera))]
public sealed class WindowProjection:MonoBehaviour {
 [Min(.01f)] public float displayWidth=.530f,displayHeight=.298125f;
 [Tooltip("Visible scene depth behind the display, in screen metres. Independent of eye distance.")]
 [Min(.01f)] public float contentDepth=2f;
 [Tooltip("Rigid, unit-scale screen frame. Its XY plane is the physical display.")]
 public Transform screenFrame;
 public Vector3 referenceEye=new Vector3(0,0,-.6f),trackerReference;
 public Quaternion trackerToScreen=Quaternion.identity;
 [Min(.001f)] public float trackerScale=1;
 public Vector3 EyeScreen{get;private set;}=new Vector3(0,0,-.6f);
 public double LastSampleTime{get;private set;}=double.NegativeInfinity;
 public float LastReceivedRealtime{get;private set;}=float.NegativeInfinity;
 bool? externalFresh;
 public bool viewportManagedExternally;
 public void SetExternalFreshness(bool fresh){externalFresh=fresh;}
 public void ClearExternalFreshness(){externalFresh=null;}
 public bool TrackingFresh{get{return externalFresh??(Time.realtimeSinceStartup-LastReceivedRealtime<.25f);}}
 Camera view;float metresPerUnit=1f;
 void Awake(){SetEye(referenceEye);}
 void LateUpdate(){SetEye(EyeScreen);FitViewport();}
 public static Vector3 DemoEye(float time){
  return new Vector3(.16f*Mathf.Sin(time*.65f),.065f*Mathf.Sin(time*.47f),-.60f+.10f*Mathf.Sin(time*.31f));
 }
 // Call from Unity's main thread; timestamp is monotonically increasing in one source clock.
 // For a restarted tracker call ResetTrackingClock before submitting its first sample.
 public bool ApplyScreenEyeSample(Vector3 eyeMetres,double timestamp){
  if(double.IsNaN(timestamp)||double.IsInfinity(timestamp)||timestamp<=LastSampleTime)return false;
  if(!SetEye(eyeMetres/metresPerUnit))return false;
  LastSampleTime=timestamp;LastReceivedRealtime=Time.realtimeSinceStartup;return true;
 }
 // Optional similarity calibration: E = E0 + s Q(q-q0). q must already use agreed axes.
 public bool ApplyTrackedSample(Vector3 trackerEye,double timestamp){
  var q=trackerToScreen;float length=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
  if(!Finite(length)||length<.000001f||!Finite(trackerScale)||trackerScale<=0)return false;
  return ApplyScreenEyeSample(referenceEye+trackerScale*(q.normalized*(trackerEye-trackerReference)),timestamp);
 }
 public void ResetTrackingClock(){LastSampleTime=double.NegativeInfinity;LastReceivedRealtime=float.NegativeInfinity;}
 public bool SetEye(Vector3 eye){return SetScaledEye(eye,metresPerUnit);}
 // The trusted fixed-window adapter supplies author-space dimensions and eye.
 // Validate physical limits in metres; ordinary scenes retain one metre per unit.
 public bool SetScaledEye(Vector3 eye,float scale){
  if(!Finite(scale)||scale<=0)return false;
  var physicalEye=eye*scale;float physicalWidth=displayWidth*scale,physicalHeight=displayHeight*scale;
  if(!Valid(eye)||!Valid(physicalEye)||physicalEye.z>=-.05f||physicalEye.sqrMagnitude>100||
    !Finite(physicalWidth)||!Finite(physicalHeight)||physicalWidth<.01f||physicalHeight<.01f||physicalWidth>10||physicalHeight>10||
    !Finite(contentDepth)||contentDepth<.01f||contentDepth>1000)return false;
  if(screenFrame&&(!Valid(screenFrame.position)||(screenFrame.lossyScale-Vector3.one).sqrMagnitude>.000001f))return false;
  float d=-eye.z,n=.01f/scale,far=d+contentDepth;
  if(!Finite(n)||n<=0||!Finite(far)||far<=n)return false;
  if(!view)view=GetComponent<Camera>();
  var rotation=screenFrame?screenFrame.rotation:Quaternion.identity;
  var origin=screenFrame?screenFrame.position:Vector3.zero;
  view.transform.SetPositionAndRotation(origin+rotation*eye,rotation);
  view.nearClipPlane=n;view.farClipPlane=far;
  view.projectionMatrix=Matrix4x4.Frustum(n*(-displayWidth*.5f-eye.x)/d,n*(displayWidth*.5f-eye.x)/d,
    n*(-displayHeight*.5f-eye.y)/d,n*(displayHeight*.5f-eye.y)/d,n,far);
  view.ResetCullingMatrix();EyeScreen=eye;metresPerUnit=scale;return true;
 }
 public void FitViewport(){
  if(!view||view.targetTexture||viewportManagedExternally)return;
  int outputWidth=Mathf.Max(1,Screen.width),outputHeight=Mathf.Max(1,Screen.height);
  float wanted=displayWidth/displayHeight;
  int width=outputWidth,height=outputHeight;
  if((float)width/height>wanted)width=Mathf.Clamp(Mathf.RoundToInt(height*wanted),1,outputWidth);
  else height=Mathf.Clamp(Mathf.RoundToInt(width/wanted),1,outputHeight);
  // A fractional viewport origin is rounded differently by rasterization and
  // Unity's projection/ray helpers. Keep the effective display on whole pixels.
  view.pixelRect=new Rect((outputWidth-width)/2,(outputHeight-height)/2,width,height);
 }
 static bool Valid(Vector3 v){return Finite(v.x)&&Finite(v.y)&&Finite(v.z);}
 static bool Finite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
 void OnDisable(){if(view){view.ResetProjectionMatrix();view.ResetCullingMatrix();view.rect=new Rect(0,0,1,1);}}
 void OnDrawGizmosSelected(){
  Gizmos.color=Color.cyan;
  var origin=screenFrame?screenFrame.position:Vector3.zero;var rot=screenFrame?screenFrame.rotation:Quaternion.identity;
  var corners=new[]{new Vector3(-displayWidth/2,-displayHeight/2,0),new Vector3(displayWidth/2,-displayHeight/2,0),new Vector3(displayWidth/2,displayHeight/2,0),new Vector3(-displayWidth/2,displayHeight/2,0)};
  for(int i=0;i<4;i++){Gizmos.DrawLine(origin+rot*corners[i],origin+rot*corners[(i+1)%4]);Gizmos.DrawLine(origin+rot*EyeScreen,origin+rot*corners[i]);}
 }
}

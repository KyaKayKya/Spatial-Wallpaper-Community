using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
[DefaultExecutionOrder(200)]
public sealed class AdaptiveRenderCadence:MonoBehaviour {
 public WindowProjection projection;
 public bool adaptive=true;
 [Range(1,12)] public int idleInterval=6;
 public float settleSeconds=.75f,motionThreshold=.0001f;
 Vector3 anchor;
 float lastMove;
 int oldInterval,oldSync,oldTarget;
 readonly Dictionary<Object,int> requests=new Dictionary<Object,int>();
 readonly List<Object> expired=new List<Object>();
 public void RequestContinuous(Object owner,bool active){RequestInterval(owner,active?1:0);}
 public void RequestInterval(Object owner,int interval){
  if(!owner)return;
  if(interval<=0)requests.Remove(owner);else requests[owner]=Mathf.Clamp(interval,1,12);
  Wake();
 }
 void OnEnable(){
  oldInterval=OnDemandRendering.renderFrameInterval;oldSync=QualitySettings.vSyncCount;oldTarget=Application.targetFrameRate;
  QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Wake();
 }
 // Content/lighting/UI changes must wake rendering even when the eye is stationary.
 public void Wake(){lastMove=Time.unscaledTime;OnDemandRendering.renderFrameInterval=1;}
 void LateUpdate(){
  if(!projection)return;
  if((projection.EyeScreen-anchor).sqrMagnitude>motionThreshold*motionThreshold){anchor=projection.EyeScreen;Wake();}
  int interval=adaptive&&Time.unscaledTime-lastMove>settleSeconds?idleInterval:1;
  expired.Clear();
  foreach(var entry in requests){if(!entry.Key)expired.Add(entry.Key);else interval=Mathf.Min(interval,entry.Value);}
  foreach(var key in expired)requests.Remove(key);
  OnDemandRendering.renderFrameInterval=interval;
 }
 void OnDisable(){OnDemandRendering.renderFrameInterval=oldInterval;QualitySettings.vSyncCount=oldSync;Application.targetFrameRate=oldTarget;}
}

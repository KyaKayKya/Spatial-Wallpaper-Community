using UnityEngine;
using System;
[DefaultExecutionOrder(0)]
public sealed class PreviewEyeDriver:MonoBehaviour {
 public WindowProjection projection;
 public AdaptiveRenderCadence cadence;
 public GameObject exhibit;
 public bool simulateMotion,allowInput=true,showHelp,externalTracking;
 float demoStart;
 void Start(){simulateMotion=Array.IndexOf(Environment.GetCommandLineArgs(),"--demo")>=0;demoStart=Time.unscaledTime;}
 void Update(){
  if(!allowInput||!projection)return;
  if(Input.GetKeyDown(KeyCode.O)&&exhibit){exhibit.SetActive(!exhibit.activeSelf);cadence.Wake();}
  if(Input.GetKeyDown(KeyCode.F1)){showHelp=!showHelp;cadence.Wake();}
  if(Input.GetKeyDown(KeyCode.F11)){Screen.fullScreen=!Screen.fullScreen;cadence.Wake();}
  if(!externalTracking&&Input.GetKeyDown(KeyCode.Space)){simulateMotion=!simulateMotion;demoStart=Time.unscaledTime;}
  if(!externalTracking&&Input.GetKeyDown(KeyCode.R)){simulateMotion=false;projection.SetEye(projection.referenceEye);projection.ResetTrackingClock();}
  // A fresh external sample takes priority over all simulated movement.
  if(externalTracking||!double.IsNegativeInfinity(projection.LastSampleTime))return;
  if(simulateMotion){projection.SetEye(WindowProjection.DemoEye(Time.unscaledTime-demoStart));return;}
  var eye=projection.EyeScreen;
  if(Input.GetMouseButton(1)){
   var rect=projection.GetComponent<Camera>().pixelRect;
   eye.x=((Input.mousePosition.x-rect.x)/rect.width-.5f)*.40f;
   eye.y=((Input.mousePosition.y-rect.y)/rect.height-.5f)*.24f;
  }
  eye.z=Mathf.Clamp(eye.z+Input.mouseScrollDelta.y*.025f,-1.2f,-.25f);
  eye.x+=Input.GetAxisRaw("Horizontal")*.12f*Time.unscaledDeltaTime;
  eye.y+=Input.GetAxisRaw("Vertical")*.12f*Time.unscaledDeltaTime;
  eye.x=Mathf.Clamp(eye.x,-.3f,.3f);eye.y=Mathf.Clamp(eye.y,-.2f,.2f);
  if(eye!=projection.EyeScreen)projection.SetEye(eye);
 }
 void OnGUI(){
  if(!showHelp)return;
  GUI.Box(new Rect(16,16,470,112),"Spatial window / simulated eye (metres)");
  GUI.Label(new Rect(28,42,446,24),"RMB / arrows: move eye    Wheel: distance    Space: auto");
  GUI.Label(new Rect(28,65,446,24),"R: reset   O: remove/restore prop   F1: help   F11: fullscreen");
  GUI.Label(new Rect(28,88,446,24),"Eye "+projection.EyeScreen.ToString("F3")+"    "+(projection.TrackingFresh?"TRACKER":double.IsNegativeInfinity(projection.LastSampleTime)?"PREVIEW":"TRACKING LOST / HOLD"));
 }
}

using UnityEngine;
using System;
using SpatialWallpaper.Sdk;
using SpatialTracking;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(40)]
 public sealed class MitaDisplayRig : MonoBehaviour {
  public WindowProjection projection;
  public Transform contentFrame;
  public bool keepHeight=true;
  public ViewportMetrics physicalViewport;
  void Start(){keepHeight=Array.IndexOf(Environment.GetCommandLineArgs(),"--keep-width")<0;}
  public void Fit(float aspect){
   projection.displayWidth=physicalViewport==null?.53f:(float)physicalViewport.Width;
   projection.displayHeight=physicalViewport==null?.53f/aspect:(float)physicalViewport.Height;
   contentFrame.localScale=Vector3.one*(float)Framing.Scale(.53,.298125,projection.displayWidth,projection.displayHeight,keepHeight);
   projection.viewportManagedExternally=true;
   projection.GetComponent<Camera>().rect=new Rect(0,0,1,1);
  }
  void LateUpdate(){Fit((float)Screen.width/Screen.height);}
 }
}

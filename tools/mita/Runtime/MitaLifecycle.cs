using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaLifecycle : MonoBehaviour {
  [Serializable] sealed class ViewCase {public int width,height;public bool keep_height,mapped,uniform_scale,fixed_camera;public float corner_error,physical_width,physical_height;}
  [Serializable] sealed class Report {public bool received_before_pause,clock_frozen,tracking_released,audio_paused,received_after_resume,audio_time_frozen,audio_resumed;public long accepted;public float audio_clip_seconds;public string calibration_status;public bool physical_viewport,paused_viewport_updated;public float display_width,display_height,corner_error;public List<ViewCase> viewports=new List<ViewCase>();}
  float CornerError(){
   var behaviour=GetComponent<MitaBehaviour>();float scale=GetComponent<MitaDisplayRig>().contentFrame.lossyScale.x,error=0;
   foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
    var point=behaviour.view.WorldToScreenPoint(behaviour.volume.FrontCorner(x,y));
    var expected=new Vector2((.5f+x*behaviour.volume.size.x*scale/behaviour.projection.displayWidth)*Screen.width,(.5f+y*behaviour.volume.size.y*scale/behaviour.projection.displayHeight)*Screen.height);
    error=Mathf.Max(error,Vector2.Distance(point,expected));
   }
   return error;
  }
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lifecycle")<0)yield break;
   var tracking=GetComponent<MitaTracking>();var behaviour=GetComponent<MitaBehaviour>();var shot=GetComponent<MitaShot>();
   yield return new WaitForSecondsRealtime(8);
   var report=new Report{received_before_pause=tracking.Fresh,accepted=tracking.Accepted};
   var rig=GetComponent<MitaDisplayRig>();bool physical=rig.physicalViewport!=null;int width=Screen.width,height=Screen.height;
   behaviour.TogglePause();float frozen=shot.Clock;float musicTime=behaviour.music?behaviour.music.time:0;
   if(physical){
    float previousWidth=behaviour.projection.displayWidth;
    Screen.SetResolution(width*3/4,height*3/4,false);yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
    report.paused_viewport_updated=rig.physicalViewport!=null&&Mathf.Abs(behaviour.projection.displayWidth-previousWidth)>.01f&&CornerError()<.05f;
   }
   yield return new WaitForSecondsRealtime(2);
   report.clock_frozen=Mathf.Abs(shot.Clock-frozen)<.00001f;report.tracking_released=tracking.Accepted==0&&!tracking.Fresh;report.audio_paused=AudioListener.pause;
   report.audio_time_frozen=behaviour.music&&Mathf.Abs(behaviour.music.time-musicTime)<.05f;
   behaviour.TogglePause();if(physical)Screen.SetResolution(width,height,false);yield return new WaitForSecondsRealtime(8);
   report.received_after_resume=tracking.Fresh&&tracking.Accepted>0;
   report.calibration_status=tracking.CalibrationStatus;report.physical_viewport=GetComponent<MitaDisplayRig>().physicalViewport!=null;
   report.display_width=behaviour.projection.displayWidth;report.display_height=behaviour.projection.displayHeight;
   yield return new WaitForEndOfFrame();
   report.corner_error=CornerError();
   report.audio_resumed=behaviour.music&&behaviour.music.isPlaying&&behaviour.music.time>musicTime+1;report.audio_clip_seconds=behaviour.music?behaviour.music.clip.length:0;
   string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");
   if(physical){
    foreach(var size in new[]{new Vector2Int(960,540),new Vector2Int(960,600),new Vector2Int(800,600),new Vector2Int(1200,500),new Vector2Int(420,746)}){
     Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
     foreach(bool keep in new[]{true,false}){
      rig.keepHeight=keep;yield return new WaitForEndOfFrame();
      var scale=rig.contentFrame.lossyScale;
      report.viewports.Add(new ViewCase{width=Screen.width,height=Screen.height,keep_height=keep,mapped=tracking.Fresh&&tracking.CalibrationStatus=="MAPPED",
       uniform_scale=Mathf.Abs(scale.x-scale.y)<.00001f&&Mathf.Abs(scale.x-scale.z)<.00001f,fixed_camera=Quaternion.Angle(behaviour.view.transform.rotation,Quaternion.identity)<.00001f,
       corner_error=CornerError(),physical_width=behaviour.projection.displayWidth,physical_height=behaviour.projection.displayHeight});
      ScreenCapture.CaptureScreenshot(Path.Combine(output,$"physical-{size.x}x{size.y}-{(keep?"height":"width")}.png"));yield return null;
     }
    }
   }
   File.WriteAllText(Path.Combine(output,"lifecycle.json"),JsonUtility.ToJson(report,true));Application.Quit();
  }
 }
}

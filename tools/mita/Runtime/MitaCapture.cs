using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaCapture : MonoBehaviour {
  [Serializable] public sealed class GeometryCase {
   public string aspect,fit,eye;
   public float corner_error_px;
   public bool floor_covers_aperture,uniform_scale,fixed_camera_rotation,scaled_light_range;
  }
  [Serializable] public sealed class Report {
   public List<GeometryCase> geometry=new List<GeometryCase>();
   public float animation_seconds,blink_range,max_eye_error_degrees,character_root_drift;
   public float idle_smile,extra_expression_range,extra_motion_seconds;
   public int extra_motion_visits;
   public float calm_seconds,body_rotation_degrees,body_translation_m;
   public float calm_body_rotation_degrees,calm_body_step_degrees,tracked_seconds;
   public int calm_unexpected_states,body_bones;
   public bool touch_hit,touch_miss,dismiss_started;
   public int click_effects,wink_effects,peak_click_particles,peak_wink_particles;
   public float click_effect_error_px;
   public float click_audio_seconds;
   public bool click_audio_playing;
  }
  IEnumerator Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--validate")<0)yield break;
   var behaviour=GetComponent<MitaBehaviour>();var look=GetComponent<MitaLook>();var shot=GetComponent<MitaShot>();var rig=GetComponent<MitaDisplayRig>();
   string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");Directory.CreateDirectory(output);
   var report=new Report();look.automatic=true;var effects=GetComponent<MitaEffects>();
   var headMesh=behaviour.actor.transform.Find("Head").GetComponent<SkinnedMeshRenderer>();int blink=headMesh.sharedMesh.GetBlendShapeIndex("Blink");
   int smile=headMesh.sharedMesh.GetBlendShapeIndex("SmileSmall"),reaction=headMesh.sharedMesh.GetBlendShapeIndex("SmileManiac");float minimumReaction=100,maximumReaction=0;
   float minimum=100,maximum=0;int previous=0;bool first=false,second=false,third=false,baseline=false,clickImage=false,winkImage=false;Vector3 originalRoot=behaviour.actor.transform.localPosition;
   var body=behaviour.actor.GetComponentsInChildren<Transform>().Where(t=>t.name=="Hips"||t.name.StartsWith("Spine")||t.name=="Chest").ToArray();
   report.body_bones=body.Length;
   yield return new WaitForEndOfFrame();
   var positions=body.Select(t=>t.localPosition).ToArray();var rotations=body.Select(t=>t.localRotation).ToArray();var previousRotations=rotations.ToArray();
   while(shot.Clock<72){
    if(shot.Clock>43&&!first){
     report.touch_miss=!behaviour.Touch(new Vector2(-1000,-1000));
     Vector2 face=behaviour.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
     report.touch_hit=behaviour.Touch(face);behaviour.Touch(face);first=true;
     report.click_effect_error_px=Vector2.Distance(face,behaviour.view.WorldToScreenPoint(effects.click.transform.position));
    }
    if(shot.Clock>50&&!second){behaviour.React();second=true;}
    if(shot.Clock>59&&!third){behaviour.React();third=true;}
    yield return new WaitForEndOfFrame();
    report.peak_click_particles=Mathf.Max(report.peak_click_particles,effects.click.particleCount);report.peak_wink_particles=Mathf.Max(report.peak_wink_particles,effects.wink.particleCount);
    if(!clickImage&&effects.click.particleCount>0){ScreenCapture.CaptureScreenshot(Path.Combine(output,"effect-click.png"));clickImage=true;}
    if(!winkImage&&effects.wink.particleCount>0){ScreenCapture.CaptureScreenshot(Path.Combine(output,"effect-wink.png"));winkImage=true;}
    if(effects.Clicks>0&&effects.clickAudio.isPlaying)report.click_audio_playing=true;
    float weight=headMesh.GetBlendShapeWeight(blink);minimum=Mathf.Min(minimum,weight);maximum=Mathf.Max(maximum,weight);
    var state=behaviour.actor.GetCurrentAnimatorStateInfo(0);
    if(shot.Clock>8&&!baseline){positions=body.Select(t=>t.localPosition).ToArray();rotations=body.Select(t=>t.localRotation).ToArray();previousRotations=rotations.ToArray();baseline=true;}
    bool calm=shot.Clock>8&&shot.Clock<40;
    if(calm){report.calm_seconds+=Time.deltaTime;if(!state.IsName("Base Layer.MitaIdle")||behaviour.actor.IsInTransition(0))report.calm_unexpected_states++;}
    for(int b=0;b<body.Length;b++){
     report.body_rotation_degrees=Mathf.Max(report.body_rotation_degrees,Quaternion.Angle(rotations[b],body[b].localRotation));
     if(calm){report.calm_body_rotation_degrees=Mathf.Max(report.calm_body_rotation_degrees,Quaternion.Angle(rotations[b],body[b].localRotation));report.calm_body_step_degrees=Mathf.Max(report.calm_body_step_degrees,Quaternion.Angle(previousRotations[b],body[b].localRotation));}
     report.body_translation_m=Mathf.Max(report.body_translation_m,Vector3.Distance(positions[b],body[b].localPosition));
     previousRotations[b]=body[b].localRotation;
    }
    if(shot.Clock>8&&shot.Clock<9)report.idle_smile=headMesh.GetBlendShapeWeight(smile);
    if(state.IsName("Base Layer.Mita EndingFace")){
     report.extra_motion_seconds+=Time.deltaTime;
     float expression=headMesh.GetBlendShapeWeight(reaction);minimumReaction=Mathf.Min(minimumReaction,expression);maximumReaction=Mathf.Max(maximumReaction,expression);
    }
    if(state.IsName("Base Layer.Mita EndingFace")&&state.fullPathHash!=previous)report.extra_motion_visits++;
    previous=state.fullPathHash;
    if(state.IsName("Base Layer.Xlmy Dismiss"))report.dismiss_started=true;
    if(shot.Clock>8){
     if(look.leftAim.weight>.999f&&look.rightAim.weight>.999f){
      float error=Mathf.Max(Vector3.Angle(look.leftEye.TransformDirection(look.leftAim.aimVector),look.eyesTarget.position-look.leftEye.position),Vector3.Angle(look.rightEye.TransformDirection(look.rightAim.aimVector),look.eyesTarget.position-look.rightEye.position));
      report.max_eye_error_degrees=Mathf.Max(report.max_eye_error_degrees,error);report.tracked_seconds+=Time.deltaTime;
     }
     report.character_root_drift=Mathf.Max(report.character_root_drift,(behaviour.actor.transform.localPosition-originalRoot).magnitude);
    }
   }
   report.animation_seconds=shot.Clock;report.blink_range=(maximum-minimum)/100;report.extra_expression_range=(maximumReaction-minimumReaction)/100;
   report.click_effects=effects.Clicks;report.wink_effects=effects.Winks;
   report.click_audio_seconds=effects.clickAudio.clip.length;
   // Keep shot sampling alive: aspect fitting also changes the light's world range.
   rig.enabled=false;look.enabled=false;
   var eyeValues=new[]{new Vector3(0,0,-.6f),new Vector3(-.12f,0,-.6f),new Vector3(.12f,0,-.6f),new Vector3(0,.05f,-.6f),new Vector3(0,-.05f,-.6f),new Vector3(0,0,-.45f),new Vector3(0,0,-.75f)};
   string[] eyeNames={"front","left","right","up","down","near","far"};
   var sizes=new[]{new Vector2Int(1280,720),new Vector2Int(1280,800),new Vector2Int(1024,768),new Vector2Int(1720,720),new Vector2Int(720,1280)};
   string[] aspects={"16x9","16x10","4x3","ultrawide","portrait"};
   for(int aspect=0;aspect<sizes.Length;aspect++){
    Screen.SetResolution(sizes[aspect].x,sizes[aspect].y,false);yield return new WaitForSeconds(.5f);
    foreach(bool keepHeight in new[]{true,false}){
     rig.keepHeight=keepHeight;rig.Fit((float)Screen.width/Screen.height);
     for(int i=0;i<eyeValues.Length;i++){
      behaviour.projection.SetEye(eyeValues[i]);yield return new WaitForEndOfFrame();
      float error=0;var volume=behaviour.volume;float scale=rig.contentFrame.lossyScale.x;
      foreach(var sign in new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)}){
       Vector3 actual=behaviour.view.WorldToScreenPoint(volume.FrontCorner(sign.x,sign.y));
       Vector2 expected=new Vector2((.5f+sign.x*volume.size.x*scale/behaviour.projection.displayWidth)*Screen.width,(.5f+sign.y*volume.size.y*scale/behaviour.projection.displayHeight)*Screen.height);
       error=Mathf.Max(error,Vector2.Distance(new Vector2(actual.x,actual.y),expected));
      }
      var floor=shot.floor;var mesh=floor.GetComponent<MeshFilter>().sharedMesh;float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minZ=float.PositiveInfinity;bool coplanar=true;
      foreach(var vertex in mesh.vertices){var world=floor.transform.TransformPoint(vertex);minX=Mathf.Min(minX,world.x);maxX=Mathf.Max(maxX,world.x);minZ=Mathf.Min(minZ,world.z);coplanar&=Mathf.Abs(world.y+.1490625f*scale)<.00001f;}
      var record=new GeometryCase{aspect=aspects[aspect],fit=keepHeight?"keep-height":"keep-width",eye=eyeNames[i],corner_error_px=error,
       floor_covers_aperture=coplanar&&minZ<.00001f&&minX<=-behaviour.projection.displayWidth/2&&maxX>=behaviour.projection.displayWidth/2,
       uniform_scale=Mathf.Abs(rig.contentFrame.lossyScale.x-rig.contentFrame.lossyScale.z)<.00001f&&Mathf.Abs(rig.contentFrame.lossyScale.y-rig.contentFrame.lossyScale.z)<.00001f,
       fixed_camera_rotation=Quaternion.Angle(behaviour.view.transform.rotation,Quaternion.identity)<.00001f,
       scaled_light_range=Mathf.Abs(shot.key.range-shot.sampledLight.range*shot.placement.lossyScale.x)<.0001f};
      report.geometry.Add(record);
      ScreenCapture.CaptureScreenshot(Path.Combine(output,record.aspect+"-"+record.fit+"-"+record.eye+".png"));yield return null;
     }
    }
   }
   File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit();
  }
 }
}

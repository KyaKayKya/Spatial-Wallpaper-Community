using System;
using System.IO;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialWallpaper.Sdk;
[DefaultExecutionOrder(10000)]public sealed class PoolReferenceCapture:MonoBehaviour {
 Transform root;Camera camera;WindowProjection projection;Vector3 scale,position,frame;float depth;JObject metrics;
 IEnumerator Start(){
  root=GameObject.Find("PoolContent").transform;camera=GetComponent<Camera>();projection=GetComponent<WindowProjection>();depth=projection.contentDepth;projection.viewportManagedExternally=true;scale=root.localScale;position=root.localPosition;var volume=root.GetComponentInChildren<DisplaySceneVolume>();frame=Vector3.Scale(volume.size,volume.transform.lossyScale);
  if(NaturalSeriesCapture.Argument("--series-output")!=null){NaturalSeriesCapture.Launch(camera);yield break;}
  for(int i=0;i<150;i++)yield return new WaitForEndOfFrame();string path=NaturalSeriesCapture.Argument("--reference-output");if(metrics==null){Application.Quit(1);yield break;}Directory.CreateDirectory(Path.GetDirectoryName(path));ScreenCapture.CaptureScreenshot(path);File.WriteAllText(Path.ChangeExtension(path,"json"),metrics.ToString());for(int i=0;i<5;i++)yield return null;Application.Quit(0);
 }
 void LateUpdate(){
  if(!root||!DisplayViewport.TryMeasure(camera.pixelRect,.53,out var viewport,out var display))return;float factor=(float)Framing.Scale(frame.x,frame.y,viewport.Width,viewport.Height,true);root.localScale=scale*factor;root.localPosition=position*factor;projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;projection.contentDepth=depth*factor;var eye=viewport.ToView(new[]{0.0,0,-.67});if(!projection.SetEye(new Vector3((float)eye[0],(float)eye[1],(float)eye[2])))return;
  metrics=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY,["scale"]=factor,["eye"]=new JArray(eye),["pixels"]=new JArray(Screen.width,Screen.height),["fixed_time"]=JValue.CreateNull(),["scene_time"]=Time.timeSinceLevelLoad,["content_depth"]=projection.contentDepth};
 }
}

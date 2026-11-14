using System;
using System.IO;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialWallpaper.Train;
using SpatialWallpaper.Sdk;
[DefaultExecutionOrder(10000)]public sealed class TrainReferenceCapture:MonoBehaviour {
 TrainJourney journey;Transform root;Camera camera;WindowProjection projection;Vector3 baseScale,basePosition,frame;float depth;JObject metrics;bool natural;
 string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
 IEnumerator Start(){
  natural=Argument("--series-output")!=null;journey=FindObjectOfType<TrainJourney>();journey.externalClock=!natural;root=GameObject.Find("SceneModel").transform;camera=GetComponent<Camera>();projection=GetComponent<WindowProjection>();depth=projection.contentDepth;projection.viewportManagedExternally=true;
  baseScale=root.localScale;basePosition=root.localPosition;var volume=root.GetComponent<DisplaySceneVolume>();frame=Vector3.Scale(volume.size,volume.transform.lossyScale);
  if(natural){NaturalSeriesCapture.Launch(camera);yield break;}
  for(int i=0;i<150;i++)yield return new WaitForEndOfFrame();
  string path=Argument("--reference-output");if(metrics==null){Application.Quit(1);yield break;}Directory.CreateDirectory(Path.GetDirectoryName(path));ScreenCapture.CaptureScreenshot(path);File.WriteAllText(Path.ChangeExtension(path,"json"),metrics.ToString());for(int i=0;i<5;i++)yield return null;Application.Quit(0);
 }
 void LateUpdate(){
  if(!journey||!DisplayViewport.TryMeasure(camera.pixelRect,.53,out var viewport,out var display))return;
  float scale=(float)Framing.Scale(frame.x,frame.y,viewport.Width,viewport.Height,true);root.localScale=baseScale*scale;root.localPosition=basePosition*scale;projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;projection.contentDepth=depth*scale;
  var eye=viewport.ToView(new[]{0.0,0,-.67});if(!projection.SetEye(new Vector3((float)eye[0],(float)eye[1],(float)eye[2])))return;if(!natural)journey.Evaluate(2.5f);
  metrics=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY,["scale"]=scale,["eye"]=new JArray(eye),["pixels"]=new JArray(Screen.width,Screen.height),["fixed_time"]=2.5,["content_depth"]=projection.contentDepth};
 }
}

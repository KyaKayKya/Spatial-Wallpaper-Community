using System;
using System.IO;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialWallpaper.Parking;
using SpatialWallpaper.Sdk;
[DefaultExecutionOrder(10000)]public sealed class ParkingReferenceCapture:MonoBehaviour {
 ParkingAtmosphere source;Camera camera;WindowProjection projection;Vector3 baseScale,basePosition;float baseDepth;JObject metrics;bool ready,natural;
 string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
 IEnumerator Start(){
  natural=Argument("--series-output")!=null;source=FindObjectOfType<ParkingAtmosphere>();source.externalClock=!natural;source.animate=natural;camera=GetComponent<Camera>();projection=GetComponent<WindowProjection>();baseDepth=projection.contentDepth;projection.screenFrame=null;projection.viewportManagedExternally=true;baseScale=source.transform.localScale;basePosition=source.transform.localPosition;
  float deadline=Time.realtimeSinceStartup+15;while(!Measure()&&Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.05f);if(!ready){Application.Quit(1);yield break;}
  if(natural){NaturalSeriesCapture.Launch(camera);yield break;}
  for(int i=0;i<150;i++)yield return new WaitForEndOfFrame();string path=Argument("--reference-output");Directory.CreateDirectory(Path.GetDirectoryName(path));ScreenCapture.CaptureScreenshot(path);File.WriteAllText(Path.ChangeExtension(path,"json"),metrics.ToString());for(int i=0;i<5;i++)yield return null;Application.Quit(0);
 }
 bool Measure(){if(!source||!camera||!DisplayViewport.TryMeasure(camera.pixelRect,.53,out var viewport,out var display))return false;float scale=(float)Framing.Scale(.53,.298125,viewport.Width,viewport.Height,true);source.transform.localScale=baseScale*scale;source.transform.localPosition=basePosition*scale;projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;projection.contentDepth=baseDepth*scale;var eye=viewport.ToView(new[]{0.0,0,-.67});ready=projection.SetEye(new Vector3((float)eye[0],(float)eye[1],(float)eye[2]));if(!natural)source.Evaluate(2.5f);metrics=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY,["scale"]=scale,["eye"]=new JArray(eye),["pixels"]=new JArray(Screen.width,Screen.height),["fixed_time"]=2.5,["content_depth"]=projection.contentDepth};return ready;}
 void LateUpdate(){if(source)Measure();}
}

using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(75)]
public sealed class LimpoolCapture:MonoBehaviour {
 bool benchmarkWaterline;float benchmarkStart,benchmarkWaterHeight;
 void LateUpdate(){
  if(!benchmarkWaterline)return;
  float t=Time.realtimeSinceStartup-benchmarkStart;
  GetComponent<WindowProjection>().SetEye(t<70?new Vector3(0,benchmarkWaterHeight+.0005f,-.6f):new Vector3(.16f*Mathf.Sin((t-70)*.7f),benchmarkWaterHeight+.002f*Mathf.Cos((t-70)*1.4f),-.675f+.275f*Mathf.Sin((t-70)*.4f)));
 }
 IEnumerator Start(){
  var args=Environment.GetCommandLineArgs();benchmarkWaterline=Array.IndexOf(args,"--benchmark-waterline")>=0&&Array.IndexOf(args,"--benchmark")>=0;
  if(benchmarkWaterline){benchmarkStart=Time.realtimeSinceStartup;benchmarkWaterHeight=GameObject.Find("WaterSurface").transform.position.y;}
  int ix=Array.IndexOf(args,"--limpool-capture");if(ix<0||ix+1>=args.Length)yield break;
  var output=Path.GetFullPath(args[ix+1]);Directory.CreateDirectory(output);
  bool video=Array.IndexOf(args,"--video")>=0,hold=Array.IndexOf(args,"--hold")>=0;
  var projection=GetComponent<WindowProjection>();var preview=GetComponent<PreviewEyeDriver>();preview.allowInput=false;preview.showHelp=false;
  GetComponent<AdaptiveRenderCadence>().enabled=false;OnDemandRendering.renderFrameInterval=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
  for(int i=0;i<30;i++)yield return new WaitForEndOfFrame();
  var eyes=new List<Vector3>{new Vector3(0,0,-.6f),new Vector3(-.16f,0,-.6f),new Vector3(.16f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.06f,-.6f),new Vector3(0,0,-.4f),new Vector3(0,0,-.95f)};
  var names=new List<string>{"front","left","right","up","down","near","far"};
  foreach(float z in new[]{-.4f,-.95f})foreach(float x in new[]{-.16f,.16f})foreach(float y in new[]{-.06f,.1f}){eyes.Add(new Vector3(x,y,z));names.Add((z==-.4f?"near":"far")+(x<0?"-left":"-right")+(y<0?"-down":"-up"));}
  bool underwater=Array.IndexOf(args,"--underwater")>=0;
  bool criticalWaterline=Array.IndexOf(args,"--waterline-critical")>=0;
  float waterHeight=GameObject.Find("WaterSurface").transform.position.y;
  if(underwater||criticalWaterline){
   eyes.Clear();names.Clear();
   var offsets=criticalWaterline?new[]{.02f,.005f,.002f,.001f,.0005f,.0001f,.00001f,.0000005f,0,-.0000005f,-.00001f,-.0001f,-.001f,-.005f,-.02f,-.055f}:new[]{.02f,.002f,0,-.002f,-.02f,-.055f};
   foreach(float z in new[]{-.4f,-.6f,-.95f})foreach(float x in new[]{-.16f,0,.16f})foreach(float offset in offsets){
    eyes.Add(new Vector3(x,waterHeight+offset,z));names.Add("eye-"+(eyes.Count-1).ToString("D3"));
   }
  }
  var architecture=GameObject.Find("LimpoolArchitecture");var meshes=architecture.GetComponentsInChildren<MeshFilter>();var matrices=Array.ConvertAll(meshes,m=>m.transform.localToWorldMatrix);
  var diagnostic=new StringBuilder();diagnostic.AppendLine("Lightmaps="+LightmapSettings.lightmaps.Length+" mode="+LightmapSettings.lightmapsMode+" sun="+RenderSettings.sun.intensity+" direction="+RenderSettings.sun.transform.forward);
  foreach(var m in meshes){var rr=m.GetComponent<Renderer>();diagnostic.AppendLine(m.name+" lightmap="+rr.lightmapIndex+" uv="+rr.lightmapScaleOffset+" material="+rr.sharedMaterial.name+" color="+rr.sharedMaterial.GetColor("_BaseColor"));}
  File.WriteAllText(Path.Combine(output,"render-diagnostics.txt"),diagnostic.ToString());
  foreach(var sampling in FindObjectsOfType<LimpoolVolumeSampling>()){
   var material=sampling.GetComponent<Renderer>().sharedMaterial;
   File.AppendAllText(Path.Combine(output,"render-diagnostics.txt"),sampling.name+" actualRaymarch="+material.GetVector("_RayMarchSettings").ToString("F5")+" maxSteps="+material.GetInt("_RayMarchMaxSteps")+"\n");
  }
  var floor=Array.Find(meshes,m=>m.name.StartsWith("Floor_Submerged"));var left=Array.Find(meshes,m=>m.name.StartsWith("Wall_Left"));var right=Array.Find(meshes,m=>m.name.StartsWith("Wall_Right"));
  var top=Array.Find(meshes,m=>m.name.StartsWith("Ceiling_Main"));var lb=left.GetComponent<Renderer>().bounds;var rb=right.GetComponent<Renderer>().bounds;var fb=floor.GetComponent<Renderer>().bounds;var tb=top.GetComponent<Renderer>().bounds;
  var corners=new[]{new Vector3(lb.max.x,fb.max.y,lb.min.z),new Vector3(rb.min.x,fb.max.y,rb.min.z),new Vector3(lb.max.x,tb.min.y,lb.min.z),new Vector3(rb.min.x,tb.min.y,rb.min.z)};
  var csv=new StringBuilder("frame,time,eye_x,eye_y,eye_z,corner_error_px,reflection_view_error,reflection_uv_error,underwater_clip_failures\n");float worst=0,reflectError=0,reflectionUV=0;int moved=0,clipFailures=0,missingReflections=0;
  bool reflectionRequired=GameObject.Find("WaterSurface").GetComponent<PoolPlanarReflection>().enabled;
  Time.captureFramerate=video?30:0;var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
  int count=video?900:eyes.Count;int frameArg=Array.IndexOf(args,"--frames");if(video&&frameArg>=0)count=int.Parse(args[frameArg+1]);
  if(Array.IndexOf(args,"--freeze-effects")>=0)Time.timeScale=0;
  int startArg=Array.IndexOf(args,"--start-frame");int startFrame=startArg>=0?int.Parse(args[startArg+1]):0;
  var path=new[]{0,1,0,2,0,3,0,4,0,5,0,6,0};
  for(int i=0;i<count;i++){
   float time=(i+startFrame)/30f;int seg=Mathf.Min((int)(time/2.5f),path.Length-2);var eye=video?(hold?eyes[0]:Vector3.Lerp(eyes[path[seg]],eyes[path[seg+1]],Mathf.SmoothStep(0,1,time/2.5f-seg))):eyes[i];
   if(video&&underwater)eye=new Vector3(.16f*Mathf.Sin(time*.7f),waterHeight+.045f*Mathf.Cos(time*.8f),-.6f+.2f*Mathf.Sin(time*.4f));
   if(video&&criticalWaterline)eye=new Vector3(.16f*Mathf.Sin(time*.7f),waterHeight+.002f*Mathf.Cos(time*1.4f),-.675f+.275f*Mathf.Sin(time*.4f));
   projection.SetEye(eye);yield return new WaitForEndOfFrame();
   var cam=GetComponent<Camera>();float error=0;
   for(int c=0;c<4;c++){var v=cam.WorldToViewportPoint(corners[c]);error=Mathf.Max(error,new Vector2((v.x-(c%2))*cam.pixelWidth,(v.y-(c/2))*cam.pixelHeight).magnitude);}
   var reflect=Array.Find(Resources.FindObjectsOfTypeAll<Camera>(),c=>c.name=="PoolReflectionCamera");float re=0;
   float uvError=0;int badClip=0;
   if(reflect&&reflect.targetTexture&&reflect.targetTexture.IsCreated()){
    float h=GameObject.Find("WaterSurface").transform.position.y;
    var expected=cam.worldToCameraMatrix*PoolPlanarReflection.Mirror(h);for(int j=0;j<16;j++)re=Mathf.Max(re,Mathf.Abs(expected[j]-reflect.worldToCameraMatrix[j]));
    foreach(float x in new[]{-.2f,.2f})foreach(float z in new[]{.05f,.5f}){
     var p=new Vector3(x,h,z);var a=cam.WorldToViewportPoint(p);var b=reflect.WorldToViewportPoint(p);
     uvError=Mathf.Max(uvError,new Vector2(a.x-b.x,a.y-b.y).magnitude);
     float side=cam.transform.position.y>=h?1:-1;
     var rejected=reflect.projectionMatrix*reflect.worldToCameraMatrix*new Vector4(x,h-side*.01f,z,1);
     if(rejected.w>0&&rejected.z>=-rejected.w)badClip++;
    }
   }else if(reflectionRequired)missingReflections++;
   reflectionUV=Mathf.Max(reflectionUV,uvError);clipFailures+=badClip;
   for(int j=0;j<meshes.Length;j++)if(matrices[j]!=meshes[j].transform.localToWorldMatrix)moved++;
   worst=Mathf.Max(worst,error);reflectError=Mathf.Max(reflectError,re);
   var old=RenderTexture.active;RenderTexture.active=null;image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();RenderTexture.active=old;
   File.WriteAllBytes(Path.Combine(output,(video?i.ToString("D4"):names[i])+".png"),image.EncodeToPNG());
   csv.AppendLine(FormattableString.Invariant($"{i},{time},{eye.x},{eye.y},{eye.z},{error},{re},{uvError},{badClip}"));
  }
  Destroy(image);File.WriteAllText(Path.Combine(output,"capture.csv"),csv.ToString());
  bool pass=worst<.01f&&reflectError<.00001f&&reflectionUV<.00001f&&moved==0&&clipFailures==0&&missingReflections==0;
  File.WriteAllText(Path.Combine(output,"checks.txt"),$"pass={pass}; actualArchitectureCornerErrorPx={worst}; reflectionViewError={reflectError}; reflectionUVError={reflectionUV}; underwaterClipFailures={clipFailures}; missingReflections={missingReflections}; reflectionRequired={reflectionRequired}; architectureChanges={moved}; resolution={Screen.width}x{Screen.height}; frames={count}; standalone={!Application.isEditor}\nSupported water-above eye range x=+-0.16m,y=-0.06..0.10m,z=-0.95..-0.40m. Simulated eye only. Matrix checks do not replace visual inspection.\n");
  Application.Quit(pass?0:2);
 }
}

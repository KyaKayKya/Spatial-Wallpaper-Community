using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Train {
 public sealed class TrainCapture:MonoBehaviour {
  IEnumerator Start(){
   var args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"--train-capture");if(ix<0||ix+1>=args.Length)yield break;
   string output=Path.GetFullPath(args[ix+1]);Directory.CreateDirectory(output);
   bool video=Array.IndexOf(args,"--train-video")>=0,hold=Array.IndexOf(args,"--train-hold")>=0;
   var p=GetComponent<WindowProjection>();var preview=GetComponent<PreviewEyeDriver>();preview.allowInput=false;preview.showHelp=false;
   var journey=FindObjectOfType<TrainJourney>();journey.externalClock=true;
   var box=FindObjectOfType<DisplaySceneVolume>();
   GetComponent<AdaptiveRenderCadence>().enabled=false;OnDemandRendering.renderFrameInterval=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
   for(int i=0;i<12;i++)yield return new WaitForEndOfFrame();
   var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.1f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f),new Vector3(0,0,-.6f),new Vector3(0,0,-.6f),new Vector3(-.2f,.1f,-.35f),new Vector3(.2f,.1f,-.35f),new Vector3(.2f,-.1f,-.35f),new Vector3(-.2f,-.1f,-.35f)};
   var names=new[]{"front","left","right","up","down","near","far","content-hidden","content-restored","near-left-up","near-right-up","near-right-down","near-left-down"};
   var path=new[]{0,1,0,2,0,3,0,4,0,5,9,10,11,12,6,0,0};
   var report=new StringBuilder("frame,time,eye_x,eye_y,eye_z,corner_error_px\n");
   int count=video?720:eyes.Length;
   float worst=0;int outside=0;bool floorLeft=false,floorRight=false;
   var filters=journey.GetComponentsInChildren<MeshFilter>();
   var restMatrices=new Matrix4x4[filters.Length];for(int n=0;n<filters.Length;n++)restMatrices[n]=filters[n].transform.localToWorldMatrix;
   int interiorMotion=0;
   foreach(var filter in journey.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices){
    var world=filter.transform.TransformPoint(v);if(!box.ContainsWorld(world,.00002f))outside++;
    if(Vector3.Distance(world,box.FrontCorner(-.5f,-.5f))<.00002f)floorLeft=true;
    if(Vector3.Distance(world,box.FrontCorner(.5f,-.5f))<.00002f)floorRight=true;
   }
   for(int i=0;i<count;i++){
    journey.gameObject.SetActive(video||i!=7);
    float t=video?i/30f:0;int segment=Mathf.Min((int)(t/1.5f),path.Length-2);
    var eye=video?(hold?p.referenceEye:Vector3.Lerp(eyes[path[segment]],eyes[path[segment+1]],Mathf.SmoothStep(0,1,t/1.5f-segment))):eyes[i];
    if(video&&!hold){eye.x*=.4f;eye.y*=.4f;eye.z=-.6f+(eye.z+.6f)*.32f;}
    p.SetEye(eye);journey.Evaluate(t);
    yield return null;yield return new WaitForEndOfFrame();
    for(int n=0;n<filters.Length;n++)if(!filters[n].name.StartsWith("WindowLayer_"))for(int k=0;k<16;k++)if(Mathf.Abs(restMatrices[n][k]-filters[n].transform.localToWorldMatrix[k])>.0000001f)interiorMotion++;
    var active=RenderTexture.active;RenderTexture.active=null;var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();RenderTexture.active=active;
    File.WriteAllBytes(Path.Combine(output,(video?i.ToString("D4"):names[i])+".png"),image.EncodeToPNG());Destroy(image);
    foreach(var filter in filters){var b=filter.sharedMesh.bounds;
     foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f}){
      var v=b.center+Vector3.Scale(b.extents,new Vector3(x,y,z));if(!box.ContainsWorld(filter.transform.TransformPoint(v),.00002f))outside++;
     }
    }
    float error=0;var cam=GetComponent<Camera>();foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var q=cam.WorldToViewportPoint(box.FrontCorner(x,y));error=Mathf.Max(error,new Vector2((q.x-x-.5f)*cam.pixelWidth,(q.y-y-.5f)*cam.pixelHeight).magnitude);
    }
    worst=Mathf.Max(worst,error);report.AppendLine(FormattableString.Invariant($"{i},{t},{eye.x},{eye.y},{eye.z},{error}"));
   }
   File.WriteAllText(Path.Combine(output,"capture.csv"),report.ToString());
   bool pass=outside==0&&interiorMotion==0&&floorLeft&&floorRight&&worst<.01f;
   File.WriteAllText(Path.Combine(output,"geometry.txt"),$"pass={pass}; outsideVertices={outside}; interiorTransformChanges={interiorMotion}; actualFloorCorners={floorLeft},{floorRight}; maxCornerErrorPx={worst}; boxRenderer={box.GetComponent<Renderer>()!=null}\n");
   File.WriteAllText(Path.Combine(output,"capture.txt"),$"Actual standalone Player framebuffer; {Screen.width}x{Screen.height}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; 30 fps deterministic playback, frames={count}. Capture duration is NOT runtime performance.\n");
   Application.Quit(pass?0:2);
  }
 }
}

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class MiniShotCapture:MonoBehaviour {
 const float RayTolerance=.00001f;
 public GameObject content;
 public DisplaySceneVolume volume;
 IEnumerator Start(){
  var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"--minishot-capture");
  if(arg<0||arg+1>=args.Length)yield break;
  // The diagnostic owns eye input only when explicitly requested, never during TCP tracking.
  if(Array.IndexOf(args,"--tracking")>=0){Debug.LogError("Use separate capture and tracking sessions");Application.Quit(2);yield break;}
  string output=Path.GetFullPath(args[arg+1]);Directory.CreateDirectory(output);
  bool video=Array.IndexOf(args,"--minishot-video")>=0;
  bool shimmer=Array.IndexOf(args,"--minishot-shimmer")>=0;
  var projection=GetComponent<WindowProjection>();var camera=GetComponent<Camera>();
  if(Array.IndexOf(args,"--minishot-legacy-grid")>=0){
   var legacy=content.GetComponentsInChildren<MeshRenderer>().First(x=>char.IsDigit(x.name[0])).sharedMaterial;
   foreach(var renderer in content.GetComponentsInChildren<MeshRenderer>()){
    if(renderer.name.StartsWith("Grid_",StringComparison.Ordinal)||renderer.name=="Back_grid_fully_in_black")renderer.enabled=true;
    if(renderer.name=="Floor"||renderer.name=="Ceiling"||renderer.name=="Wall_left"||renderer.name=="Wall_right")renderer.sharedMaterial=legacy;
   }
  }
  var preview=GetComponent<PreviewEyeDriver>();preview.allowInput=false;preview.showHelp=false;preview.simulateMotion=false;
  GetComponent<AdaptiveRenderCadence>().enabled=false;OnDemandRendering.renderFrameInterval=1;
  QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
  for(int i=0;i<8;i++)yield return new WaitForEndOfFrame();
  var meshes=content.GetComponentsInChildren<MeshFilter>();
  var matrices=meshes.Select(x=>x.transform.localToWorldMatrix).ToArray();
  var discs=meshes.Where(x=>char.IsDigit(x.name[0])).ToArray();
  bool floorLeft=false,floorRight=false,floorHeight=true;
  foreach(var f in meshes.Where(x=>x.name=="Floor"))foreach(var v in f.sharedMesh.vertices){
   var world=f.transform.TransformPoint(v);
   floorLeft|=Vector3.Distance(world,volume.FrontCorner(-.5f,-.5f))<.000001f;
   floorRight|=Vector3.Distance(world,volume.FrontCorner(.5f,-.5f))<.000001f;
   floorHeight&=Mathf.Abs(volume.transform.InverseTransformPoint(world).y+volume.size.y/2)<.000001f;
  }
  int foreground=discs.Count(x=>projection.screenFrame.InverseTransformPoint(x.transform.TransformPoint(x.sharedMesh.bounds.center)).z<0);
  var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.1f,-.6f),new Vector3(0,0,-.25f),new Vector3(0,0,-1.2f),new Vector3(-.12f,.08f,-.3f),new Vector3(.12f,-.08f,-.3f),new Vector3(0,0,-.6f),new Vector3(0,0,-.6f)};
  var names=new[]{"front","left","right","up","down","near","far","near-left-up","near-right-down","content-hidden","content-restored"};
  int[] path={0,1,0,2,0,3,0,4,0,5,7,8,6,0};
  int count=shimmer?64:video?240:eyes.Length;float worst=0,parallax=0,rayError=0,rayDirectionError=0;int changes=0;
  var csv=new StringBuilder("frame,eye_x,eye_y,eye_z,corner_error_px,target_error_px,ray_error_m,ray_direction_cross\n");
  for(int frame=0;frame<count;frame++){
   float t=video?frame/(float)(count-1)*(path.Length-1):0;
   int segment=Mathf.Min((int)t,path.Length-2);
   var eye=shimmer?new Vector3((frame-31.5f)*.0002f,.03f,-.6f):video?Vector3.Lerp(eyes[path[segment]],eyes[path[segment+1]],Mathf.SmoothStep(0,1,t-segment)):eyes[frame];
   content.SetActive(shimmer||video||frame!=9);
   if(!projection.SetEye(eye))throw new InvalidOperationException("Capture eye rejected");
   yield return null;yield return new WaitForEndOfFrame();
   float corner=0,targetError=0;
   foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
    var q=camera.WorldToViewportPoint(volume.FrontCorner(x,y));
    corner=Mathf.Max(corner,new Vector2((q.x-x-.5f)*camera.pixelWidth,(q.y-y-.5f)*camera.pixelHeight).magnitude);
   }
   foreach(var disc in discs){
    var world=disc.transform.TransformPoint(disc.sharedMesh.bounds.center);
    var v=projection.screenFrame.InverseTransformPoint(world);float d=-eye.z;
    var q=camera.WorldToViewportPoint(world);
    float x=(d*v.x+v.z*eye.x)/(d+v.z)/projection.displayWidth+.5f;
    float y=(d*v.y+v.z*eye.y)/(d+v.z)/projection.displayHeight+.5f;
    targetError=Mathf.Max(targetError,new Vector2((q.x-x)*camera.pixelWidth,(q.y-y)*camera.pixelHeight).magnitude);
   }
   // Independent of WorldToViewportPoint: ask Unity for the actual pixel ray,
   // then verify that it passes through the physical pixel S and originates
   // on the line from E to S. A near-plane ray origin need not equal E.
   float frameRayError=0,frameDirectionError=0;
   for(int x=0;x<=4;x++)for(int y=0;y<=2;y++){
    float u=x/4f,v=y/2f;
    var screen=projection.screenFrame.TransformPoint(new Vector3((u-.5f)*projection.displayWidth,(v-.5f)*projection.displayHeight,0));
    var eyeWorld=projection.screenFrame.TransformPoint(eye);
    var ray=camera.ViewportPointToRay(new Vector3(u,v,0));
    frameRayError=Mathf.Max(frameRayError,Vector3.Cross(screen-ray.origin,ray.direction.normalized).magnitude);
    frameRayError=Mathf.Max(frameRayError,Vector3.Cross(eyeWorld-ray.origin,ray.direction.normalized).magnitude);
    frameDirectionError=Mathf.Max(frameDirectionError,Vector3.Cross((screen-eyeWorld).normalized,ray.direction.normalized).magnitude);
   }
   for(int i=0;i<meshes.Length;i++)for(int n=0;n<16;n++)if(Mathf.Abs(matrices[i][n]-meshes[i].transform.localToWorldMatrix[n])>.0000001f)changes++;
   worst=Mathf.Max(worst,corner);parallax=Mathf.Max(parallax,targetError);rayError=Mathf.Max(rayError,frameRayError);rayDirectionError=Mathf.Max(rayDirectionError,frameDirectionError);
   csv.AppendLine(FormattableString.Invariant($"{frame},{eye.x},{eye.y},{eye.z},{corner},{targetError},{frameRayError},{frameDirectionError}"));
   var active=RenderTexture.active;RenderTexture.active=null;
   var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
   image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();RenderTexture.active=active;
   File.WriteAllBytes(Path.Combine(output,((video||shimmer)?frame.ToString("D4"):names[frame])+".png"),image.EncodeToPNG());Destroy(image);
  }
  content.SetActive(true);
  // Verify negative depth is retained, and a disc passed by the eye clips naturally.
  var hero=discs.OrderBy(x=>x.sharedMesh.bounds.center.z).First();
  var center=hero.transform.TransformPoint(hero.sharedMesh.bounds.center);
  float heroZ=projection.screenFrame.InverseTransformPoint(center).z;
  projection.SetEye(new Vector3(0,0,heroZ+.02f));
  bool behindEye=camera.WorldToViewportPoint(center).z<0;
  projection.SetEye(projection.referenceEye);projection.ResetTrackingClock();
  bool samples=projection.ApplyScreenEyeSample(projection.referenceEye,1)&&!projection.ApplyScreenEyeSample(projection.referenceEye,1)&&!projection.ApplyScreenEyeSample(new Vector3(float.NaN,0,-.6f),2);
  projection.ResetTrackingClock();
  // Unity's single-precision inverse projection used by ViewportPointToRay
  // contributes a few micrometres on interpolated distant eyes; allow 10 um.
  bool pass=meshes.Length==28&&discs.Length==10&&foreground==2&&floorLeft&&floorRight&&floorHeight&&changes==0&&worst<.01f&&parallax<.01f&&rayError<RayTolerance&&rayDirectionError<RayTolerance&&behindEye&&samples&&!volume.showGuide&&!volume.GetComponent<Renderer>()&&!volume.GetComponent<Collider>();
  File.WriteAllText(Path.Combine(output,"capture.csv"),csv.ToString());
  File.WriteAllText(Path.Combine(output,"geometry.json"),"{\"pass\":"+pass.ToString().ToLowerInvariant()+",\"maxCornerErrorPx\":"+worst.ToString(CultureInfo.InvariantCulture)+",\"maxTargetErrorPx\":"+parallax.ToString(CultureInfo.InvariantCulture)+",\"maxRayErrorMetres\":"+rayError.ToString(CultureInfo.InvariantCulture)+",\"maxRayDirectionCross\":"+rayDirectionError.ToString(CultureInfo.InvariantCulture)+",\"foregroundTargets\":"+foreground+",\"heroDepthMetres\":"+heroZ.ToString(CultureInfo.InvariantCulture)+",\"behindEyeClips\":"+behindEye.ToString().ToLowerInvariant()+",\"floorCorners\":"+(floorLeft&&floorRight&&floorHeight).ToString().ToLowerInvariant()+",\"contentTransformChanges\":"+changes+",\"sampleRejection\":"+samples.ToString().ToLowerInvariant()+"}\n");
  File.WriteAllText(Path.Combine(output,"conditions.txt"),$"Actual standalone Player framebuffer; Unity {Application.unityVersion}; {Screen.width}x{Screen.height}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; {QualitySettings.activeColorSpace}; screen={projection.displayWidth}x{projection.displayHeight}; frames={count}. Video is deterministic 30 fps capture, not a real-time performance measurement.\n");
  Application.Quit(pass?0:2);
 }
}

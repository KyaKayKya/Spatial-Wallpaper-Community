using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Parking {
 // Explicit opt-in capture. Time is fixed per frame; encoding speed is not a performance result.
 public sealed class ParkingCapture:MonoBehaviour {
  IEnumerator Start(){
   var args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"--parking-capture");if(ix<0||ix+1>=args.Length)yield break;
   string output=Path.GetFullPath(args[ix+1]);Directory.CreateDirectory(output);
   bool video=Array.IndexOf(args,"--parking-video")>=0,hold=Array.IndexOf(args,"--parking-hold")>=0;
   if(Array.IndexOf(args,"--parking-box-guide")>=0)CreateGuide();
   var projection=GetComponent<WindowProjection>();var preview=GetComponent<PreviewEyeDriver>();preview.allowInput=false;preview.showHelp=false;
   var content=FindObjectOfType<ParkingAtmosphere>();content.externalClock=true;
   GetComponent<AdaptiveRenderCadence>().enabled=false;OnDemandRendering.renderFrameInterval=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
   for(int i=0;i<12;i++)yield return new WaitForEndOfFrame();
   var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,.10f,-.6f),new Vector3(0,-.10f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f),new Vector3(0,0,-.6f),new Vector3(0,0,-.6f),new Vector3(0,0,-.6f),new Vector3(0,0,-.6f)};
   var names=new[]{"front","left","right","up","down","near","far","lamp-dim","screen-alternate","hidden-tvs","restored-tvs"};
   var report=new System.Text.StringBuilder("frame,time,eye_x,eye_y,eye_z,corner_error_px,screen0,screen1,lamp\n");
   int count=video?360:names.Length;
   for(int i=0;i<count;i++){
    float time=video?i/30f:i==7?10.1f:i==8?4.2f:0;
    var eye=video?(hold?projection.referenceEye:WindowProjection.DemoEye(time)):eyes[i];
    content.SetTelevisionsVisible(video||i!=9);
    projection.SetEye(eye);content.Evaluate(time);
    yield return null;yield return new WaitForEndOfFrame();
    var active=RenderTexture.active;RenderTexture.active=null;
    var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();RenderTexture.active=active;
    File.WriteAllBytes(Path.Combine(output,(video?i.ToString("D4"):names[i])+".png"),image.EncodeToPNG());Destroy(image);
    float error=0;var cam=GetComponent<Camera>();
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var q=cam.WorldToViewportPoint(FindObjectOfType<DisplaySceneVolume>().FrontCorner(x,y));
     error=Mathf.Max(error,new Vector2((q.x-x-.5f)*Screen.width,(q.y-y-.5f)*Screen.height).magnitude);
    }
    report.AppendLine(FormattableString.Invariant($"{i},{time},{eye.x},{eye.y},{eye.z},{error},{ParkingAtmosphere.ScreenSignal(time,0)},{ParkingAtmosphere.ScreenSignal(time,1)},{ParkingAtmosphere.LampSignal(time)}"));
   }
   File.WriteAllText(Path.Combine(output,"capture.csv"),report.ToString());
   File.WriteAllText(Path.Combine(output,"capture.txt"),$"Actual Player framebuffer; {Screen.width}x{Screen.height}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; Linear; deterministic 30 fps playback; frames={count}; holdEye={hold}. Capture wall time is not a runtime benchmark.\n");
   Application.Quit();
  }
  void CreateGuide(){
   var box=FindObjectOfType<DisplaySceneVolume>();var material=new Material(Shader.Find("Parking/Lamp"));material.SetColor("_LampEmission",new Color(.1f,1,.8f));
   var points=new Vector3[8];int i=0;
   foreach(float z in new[]{0f,box.size.z})foreach(float y in new[]{-.5f,.5f})foreach(float x in new[]{-.5f,.5f})points[i++]=box.transform.TransformPoint(new Vector3(x*box.size.x,y*box.size.y,z));
   foreach(var edge in new[]{new[]{0,1},new[]{1,3},new[]{3,2},new[]{2,0},new[]{4,5},new[]{5,7},new[]{7,6},new[]{6,4},new[]{0,4},new[]{1,5},new[]{2,6},new[]{3,7}}){
    var line=new GameObject("CaptureOnlyBoxGuide").AddComponent<LineRenderer>();line.sharedMaterial=material;line.positionCount=2;line.SetPositions(new[]{points[edge[0]],points[edge[1]]});line.startWidth=line.endWidth=.00065f;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
   }
  }
 }
}

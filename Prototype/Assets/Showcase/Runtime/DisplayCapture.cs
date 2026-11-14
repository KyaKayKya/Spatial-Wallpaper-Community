using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

// Opt-in evidence capture from the actual player backbuffer, after presentation rendering.
public sealed class DisplayCapture : MonoBehaviour {
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Install(){
  if(Array.IndexOf(Environment.GetCommandLineArgs(),"--capture-output")>=0)
   DontDestroyOnLoad(new GameObject("DisplayEvidenceCapture",typeof(DisplayCapture)));
 }
 static void CaptureRenderTexture(Camera camera,string path){
  var old=camera.targetTexture;var active=RenderTexture.active;
  var target=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=2};
  var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
  try{
   camera.targetTexture=target;camera.Render();RenderTexture.active=target;
   image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();
   File.WriteAllBytes(path,image.EncodeToPNG());
  }finally{camera.targetTexture=old;RenderTexture.active=active;target.Release();Destroy(target);Destroy(image);}
 }
 IEnumerator Start(){
  // Let startup scene selection complete before acquiring scene-local camera references.
  yield return null;
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--capture-output");
  if(index<0||index+1>=args.Length){Debug.LogError("Missing capture output directory");yield break;}
  string output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);
  var projection=FindObjectOfType<WindowProjection>();
  int eyeIndex=Array.IndexOf(args,"--capture-eye");
  if(eyeIndex>=0){
   if(eyeIndex+3>=args.Length)throw new ArgumentException("Capture eye needs three coordinates");
   var culture=System.Globalization.CultureInfo.InvariantCulture;
   var eye=new Vector3(float.Parse(args[eyeIndex+1],culture),float.Parse(args[eyeIndex+2],culture),float.Parse(args[eyeIndex+3],culture));
   if(!projection.SetEye(eye))throw new ArgumentException("Capture eye is outside projection limits");projection.referenceEye=eye;
  }
  var driver=FindObjectOfType<PreviewEyeDriver>();if(driver){driver.allowInput=false;driver.showHelp=false;}
  var cadence=FindObjectOfType<AdaptiveRenderCadence>();if(cadence)cadence.enabled=false;
  QualitySettings.vSyncCount=0;Application.targetFrameRate=60;OnDemandRendering.renderFrameInterval=1;
  bool sequence=Array.IndexOf(args,"--capture-sequence")>=0;
  bool fixedEye=Array.IndexOf(args,"--capture-fixed-eye")>=0;
  // Each iteration resumes at the next Update and samples that frame's end. Fix time so PNG writes
  // cannot accelerate water or other content animation relative to the eye path.
  if(sequence)Time.captureDeltaTime=1f/30f;
  var timing=new System.Text.StringBuilder("frame,eye_time,content_time\n");
  bool views=Array.IndexOf(args,"--capture-views")>=0;
  var names=new[]{"front","left","right","up","down","near","far","empty","restored"};
  float scale=projection.displayWidth/.53f;var origin=projection.referenceEye;
  var eyes=new[]{origin,origin+Vector3.left*.20f*scale,origin+Vector3.right*.20f*scale,origin+Vector3.up*.12f*scale,origin+Vector3.down*.12f*scale,origin+Vector3.forward*.25f*scale,origin+Vector3.back*.35f*scale,origin,origin};
  var report=new System.Text.StringBuilder("name,eyeX,eyeY,eyeZ,maxCornerError,exhibitActive,variantOwner\n");
  projection.SetEye(projection.referenceEye);
  for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
  int count=sequence?240:views?names.Length:1;
  for(int i=0;i<count;i++){
   if(views&&!sequence&&driver&&driver.exhibit){if(i==7)driver.exhibit.SetActive(false);if(i==8)driver.exhibit.SetActive(true);}
   projection.SetEye(sequence&&!fixedEye?WindowProjection.DemoEye(i/30f):views?eyes[i]:projection.referenceEye);
   // Move on Update, let LateUpdate finish the off-axis projection, then read the backbuffer.
   yield return null;yield return new WaitForEndOfFrame();
   var active=RenderTexture.active;RenderTexture.active=null;
   var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
   image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();
   RenderTexture.active=active;
   string name=sequence?i.ToString("D4"):views?names[i]:"player";
   File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
   if(sequence)timing.AppendLine(FormattableString.Invariant($"{i},{i/30f},{Time.timeSinceLevelLoad}"));
   Destroy(image);
   if(!sequence)CaptureRenderTexture(projection.GetComponent<Camera>(),Path.Combine(output,views?name+"-render-texture.png":"render-texture.png"));
   if(views&&!sequence){
    float error=0;var camera=projection.GetComponent<Camera>();
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var q=camera.WorldToViewportPoint(projection.screenFrame.TransformPoint(new Vector3(x*projection.displayWidth,y*projection.displayHeight,0)));
     error=Mathf.Max(error,Vector2.Distance(new Vector2(q.x,q.y),new Vector2(x+.5f,y+.5f)));
    }
    var eye=projection.EyeScreen;var receiver=FindObjectOfType<CabinetLightingReceivers>();
    report.AppendLine(FormattableString.Invariant($"{name},{eye.x},{eye.y},{eye.z},{error},{(driver&&driver.exhibit&&driver.exhibit.activeSelf)},{(receiver&&receiver.Owner?receiver.Owner.name:"empty")}"));
   }
  }
  File.WriteAllText(Path.Combine(output,"capture.txt"),$"Actual player backbuffer; {Screen.width}x{Screen.height}; {QualitySettings.activeColorSpace}; {SystemInfo.graphicsDeviceType}; frames={count}; eye={projection.EyeScreen}\n");
  if(views&&!sequence)File.WriteAllText(Path.Combine(output,"views.csv"),report.ToString());
  if(sequence)File.WriteAllText(Path.Combine(output,"sequence-timing.csv"),timing.ToString());
  Application.Quit();
 }
}

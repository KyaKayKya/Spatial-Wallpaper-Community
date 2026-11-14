using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaBehaviour : MonoBehaviour {
  public Animator actor;
  public WindowProjection projection;
  public Transform placement;
  public DisplaySceneVolume volume;
  public Camera view;
  public AudioSource music;
  public bool managed;
  public Func<Vector2,Vector2?> MapCursor;
  public bool TryMapCursor(Vector2 screen,out Vector2 cursor){var mapped=MapCursor==null?screen:MapCursor(screen);cursor=mapped??Vector2.zero;return mapped.HasValue;}
  float blinkAt=9;
  bool demo;
  bool automated;
  public bool InputEnabled=>!automated;
  float lastTouch=-10;
  public void React(){GetComponent<MitaGestures>().Play(5);}
  public void TogglePause(){
   bool paused=Time.timeScale!=0;GetComponent<MitaTracking>().SetPaused(paused);
   Time.timeScale=paused?0:1;AudioListener.pause=paused;
  }
  IEnumerator Start(){
   Application.targetFrameRate=60;
   demo=Array.IndexOf(Environment.GetCommandLineArgs(),"--demo")>=0;
   var args=Environment.GetCommandLineArgs();automated=Array.Exists(args,a=>a=="--record"||a=="--validate"||a=="--gaze"||a=="--gestures"||a=="--benchmark"||a=="--capture"||a=="--lifecycle"||a=="--desktop-test"||a=="--library-smoke"||a=="--desktop-smoke"||a=="--interactions-v2");
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--capture")>=0){
    string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");Directory.CreateDirectory(output);
    yield return new WaitForSeconds(8);
    yield return new WaitForEndOfFrame();
    ScreenCapture.CaptureScreenshot(Path.Combine(output,"front.png"));
    yield return null;
    File.WriteAllText(Path.Combine(output,"runtime.txt"),"state="+actor.GetCurrentAnimatorStateInfo(0).fullPathHash+" position="+placement.position+" scale="+placement.localScale+"\n");
    Application.Quit();
   }
  }
  void Update(){
   bool idle=actor.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.MitaIdle")&&!actor.IsInTransition(0);
   if(!idle)blinkAt=Mathf.Max(blinkAt,Time.time+2.5f);
   if(idle&&Time.time>=blinkAt){actor.SetTrigger("Blink");blinkAt=Time.time+UnityEngine.Random.Range(2.5f,7f);}
   if(automated){if(demo&&Time.timeScale!=0&&!GetComponent<MitaTracking>().enabled){float t=GetComponent<MitaShot>().Clock-6.5f;if(t>=0)projection.SetEye(WindowProjection.DemoEye(t));}return;}
   if(managed){if(Time.timeScale!=0&&Input.GetMouseButtonDown(0)&&TryMapCursor(Input.mousePosition,out var cursor)&&view.pixelRect.Contains(cursor))Touch(cursor);return;}
   if(Input.GetKeyDown(KeyCode.Escape))Application.Quit();
   if(Input.GetKeyDown(KeyCode.M)&&music)music.mute=!music.mute;
   if(Input.GetKeyDown(KeyCode.Space))TogglePause();
   if(Input.GetKeyDown(KeyCode.F11))Screen.fullScreen=!Screen.fullScreen;
   if(Time.timeScale!=0&&Input.GetMouseButtonDown(0))Touch(Input.mousePosition);
   if(Input.GetKeyDown(KeyCode.R)){actor.Rebind();GetComponent<MitaShot>().Restart();GetComponent<MitaLook>().ResetGaze();blinkAt=Time.time+9;lastTouch=-10;projection.SetEye(projection.referenceEye);if(music){music.Stop();music.Play();}}
   if(Input.GetKeyDown(KeyCode.Tab))demo=!demo;
   if(Time.timeScale!=0&&!GetComponent<MitaTracking>().enabled){
    if(demo){float elapsed=GetComponent<MitaShot>().Clock-6.5f;if(elapsed>=0)projection.SetEye(WindowProjection.DemoEye(elapsed));}
    else{
     Vector3 eye=projection.EyeScreen;float step=.2f*Time.deltaTime;
     if(Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.A))eye.x-=step;
     if(Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.D))eye.x+=step;
     if(Input.GetKey(KeyCode.UpArrow)||Input.GetKey(KeyCode.W))eye.y+=step;
     if(Input.GetKey(KeyCode.DownArrow)||Input.GetKey(KeyCode.S))eye.y-=step;
     eye.z+=Input.mouseScrollDelta.y*.025f;
     eye.x=Mathf.Clamp(eye.x,-.22f,.22f);eye.y=Mathf.Clamp(eye.y,-.12f,.12f);eye.z=Mathf.Clamp(eye.z,-.9f,-.35f);
     projection.SetEye(eye);
    }
   }
  }
  public bool Touch(Vector2 cursor){
   if(Time.timeScale==0||GetComponent<MitaShot>().Clock<6.5f||GetComponent<MitaGestures>().Busy)return false;
   if(GetComponent<MitaInteractions>().Accessory(cursor))return true;
   var look=GetComponent<MitaLook>();var center=view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
   var side=view.WorldToScreenPoint(look.head.position+view.transform.right*.11f*placement.lossyScale.x);
   float radius=Mathf.Abs(side.x-view.WorldToScreenPoint(look.head.position).x);
   if(center.z<=0||(cursor-new Vector2(center.x,center.y)).sqrMagnitude>radius*radius)return false;
   actor.SetTrigger("Blink");look.Glance();
   GetComponent<MitaEffects>().Click(cursor);
   if(Time.time-lastTouch<.6f){GetComponent<MitaGestures>().Play(4);lastTouch=-10;}else lastTouch=Time.time;
   return true;
  }
  void OnDestroy(){if(!managed){Time.timeScale=1;AudioListener.pause=false;}}
 }
}

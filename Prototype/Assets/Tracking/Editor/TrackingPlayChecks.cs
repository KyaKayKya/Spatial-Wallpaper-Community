using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpatialTracking;

// Opt-in batch Play Mode check. No lifecycle test hooks are added to the runtime host.
[InitializeOnLoad]
public static class TrackingPlayChecks {
 const string Key="SpatialTrackingPlayChecks";
 static TrackingEyeDriver driver;static RuntimeTrackingHost previous;static int phase;static double began;
 static TrackingPlayChecks(){if(SessionState.GetBool(Key,false)){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
 public static void Run(){
  EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
 }
 static RuntimeTrackingHost Host(){return (RuntimeTrackingHost)typeof(TrackingEyeDriver).GetField("host",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(driver);}
 static void Released(){
  var thread=(Thread)typeof(RuntimeTrackingHost).GetField("thread",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(previous);
  var child=typeof(RuntimeTrackingHost).GetField("child",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(previous);
  if(thread.IsAlive||child!=null)throw new Exception("Worker survived disable");
 }
 static void Finish(string result,int code){
  Directory.CreateDirectory("../Validation/Integration");
  File.WriteAllText("../Validation/Integration/lifecycle.txt",result+"\n");
  SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);
 }
 static void Tick(){
  try{
   if(EditorApplication.timeSinceStartup-began>45){Finish("FAIL lifecycle timeout phase="+phase,1);return;}
   if(!EditorApplication.isPlaying)return;
   if(!driver)driver=UnityEngine.Object.FindObjectOfType<TrackingEyeDriver>();
   if(!driver||driver.State!="TRACKING")return;
   if(phase==0){previous=Host();driver.enabled=false;Released();driver.enabled=true;phase=1;}
   else if(phase==1){previous=Host();driver.gameObject.SetActive(false);Released();driver.gameObject.SetActive(true);phase=2;}
   else Finish("PASS component disable and GameObject disable stop owned thread/process; both re-enable with fresh TCP samples",0);
  }catch(Exception e){Finish("FAIL "+e.GetType().Name+": "+e.Message,1);}
 }
}

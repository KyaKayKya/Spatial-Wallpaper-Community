using System;using System.IO;using System.Diagnostics;using System.Runtime.InteropServices;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk;
[InitializeOnLoad]
public static class StageEStoragePlay {
 static double began;
 [DllImport("ntdll.dll")]static extern int NtSuspendProcess(IntPtr process);
 static StageEStoragePlay(){EditorApplication.update+=Observe;}
 static void Observe(){
  if(!SessionState.GetBool("SDK_E_PLAY",false))return;
  if(began==0)began=EditorApplication.timeSinceStartup;
  int completed=SessionState.GetInt("SDK_E_PLAY_ROUNDS",0);
  int interrupted=SessionState.GetInt("SDK_E_PLAY_INTERRUPTED",0);
  if(interrupted!=0){
   int state=Storage.State(interrupted);
   if(state==5){
    if(Storage.Error(interrupted)!="Closed"||!Storage.Release(interrupted)||Storage.BeginRead("alpha")!=0){UnityEngine.Debug.LogError("ordinary-Play-broker-crash-state");EditorApplication.Exit(34);return;}
    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("SDK_E_OUTPUT"),"play-crash.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"state",state},{"error","Closed"},{"released",true},{"new_request",0}}.ToString());
    SessionState.SetInt("SDK_E_PLAY_INTERRUPTED",0);EditorApplication.ExitPlaymode();return;
   }
   if(EditorApplication.timeSinceStartup-began>25){UnityEngine.Debug.LogError("ordinary-Play-broker-crash-timeout");EditorApplication.Exit(35);}return;
  }
  if(SessionState.GetBool("SDK_E_PLAY_STOPPING",false)&&!EditorApplication.isPlayingOrWillChangePlaymode){
   int pid=SessionState.GetInt("SDK_E_PLAY_PID",0);bool alive=false;try{using(var process=Process.GetProcessById(pid))alive=!process.HasExited;}catch(ArgumentException){}
   if(alive){UnityEngine.Debug.LogError("ordinary-Play-broker-leaked");EditorApplication.Exit(32);return;}
   SessionState.SetBool("SDK_E_PLAY_STOPPING",false);
   if(completed==2){SessionState.SetBool("SDK_E_PLAY",false);EditorApplication.Exit(0);return;}
   EditorApplication.EnterPlaymode();return;
  }
  var author=UnityEngine.Object.FindObjectOfType<StageEStorageAuthor>();
  if(EditorApplication.isPlaying&&author&&author.phase==5){
   if(SessionState.GetBool("SDK_E_PLAY_STOPPING",false))return;
   int pid=0;foreach(var process in Process.GetProcessesByName("PlayBroker")){using(process){if(string.Equals(process.MainModule.FileName,Environment.GetEnvironmentVariable("SDK_PLAY_BROKER"),StringComparison.OrdinalIgnoreCase))pid=process.Id;}}
   if(pid==0){UnityEngine.Debug.LogError("ordinary-Play-missing-broker");EditorApplication.Exit(33);return;}
   string path=Path.Combine(Environment.GetEnvironmentVariable("SDK_E_OUTPUT"),"play.json");var rounds=File.Exists(path)?JArray.Parse(File.ReadAllText(path)):new JArray();rounds.Add(new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"phase",author.phase},{"result",author.result},{"version",author.version},{"broker_pid",pid}});File.WriteAllText(path,rounds.ToString());
   SessionState.SetInt("SDK_E_PLAY_ROUNDS",completed+1);SessionState.SetInt("SDK_E_PLAY_PID",pid);SessionState.SetBool("SDK_E_PLAY_STOPPING",true);
   if(completed==0){using(var process=Process.GetProcessById(pid)){if(NtSuspendProcess(process.Handle)!=0)throw new Exception("test-cannot-suspend-owned-broker");int request=Storage.BeginRead("alpha");process.Kill();if(request==0)throw new Exception("test-missing-pending-request");SessionState.SetInt("SDK_E_PLAY_INTERRUPTED",request);}}
   else EditorApplication.ExitPlaymode();
  }else if(EditorApplication.timeSinceStartup-began>25){SessionState.SetBool("SDK_E_PLAY",false);UnityEngine.Debug.LogError("ordinary-Play-storage-author-timeout");EditorApplication.Exit(31);}
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Ordinary Play author").AddComponent<StageEStorageAuthor>();SessionState.SetBool("SDK_E_PLAY",true);SessionState.SetInt("SDK_E_PLAY_ROUNDS",0);SessionState.SetInt("SDK_E_PLAY_INTERRUPTED",0);SessionState.SetBool("SDK_E_PLAY_STOPPING",false);EditorApplication.EnterPlaymode();}
}

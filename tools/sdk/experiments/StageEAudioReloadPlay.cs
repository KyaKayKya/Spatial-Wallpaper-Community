using System;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
[InitializeOnLoad]
public static class StageEAudioReloadPlay {
 static double began;static bool reset;
 static void Mark(string name,string value){string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,name),value);}
 static StageEAudioReloadPlay(){
  EditorApplication.update+=Observe;
  EditorApplication.playModeStateChanged+=state=>{
   if(state!=PlayModeStateChange.EnteredEditMode||!SessionState.GetBool("SDK_E_STOPPING_FOR_RELOAD",false)||SessionState.GetBool("SDK_E_RELOAD_REQUESTED",false))return;
   Mark("entered-edit","old Play exited");SessionState.SetBool("SDK_E_RELOAD_REQUESTED",true);
   if(Environment.GetEnvironmentVariable("SDK_E_SKIP_RELOAD")=="1"){Mark("reload-skipped","negative fixture");return;}
   Mark("reload-called","RequestScriptReload in edit mode; global preference unchanged");EditorUtility.RequestScriptReload();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
  };
  AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(SessionState.GetBool("SDK_E_RELOAD_REQUESTED",false))Mark("before-reload","beforeAssemblyReload");};
  AssemblyReloadEvents.afterAssemblyReload+=()=>{if(SessionState.GetBool("SDK_E_RELOAD_REQUESTED",false)){SessionState.SetBool("SDK_E_RELOAD_OCCURRED",true);Mark("after-reload","playing="+EditorApplication.isPlaying);}};
 }
 static void Observe(){
  if(!SessionState.GetBool("SDK_E_AUDIO_RELOAD",false))return;
  if(began==0)began=EditorApplication.timeSinceStartup;
  string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");var author=UnityEngine.Object.FindObjectOfType<StageEAudioFailureAuthor>();
  if(!EditorApplication.isPlaying&&SessionState.GetBool("SDK_E_RELOAD_OCCURRED",false)&&!SessionState.GetBool("SDK_E_REENTERED",false)){SessionState.SetBool("SDK_E_REENTERED",true);EditorApplication.EnterPlaymode();return;}
  if(EditorApplication.isPlaying&&author){
   if(!SessionState.GetBool("SDK_E_RELOAD_REQUESTED",false)){
    if(author.low>0&&!File.Exists(Path.Combine(output,"heard")))File.WriteAllText(Path.Combine(output,"heard"),"ready");
    if(File.Exists(Path.Combine(output,"reload-request"))&&!SessionState.GetBool("SDK_E_STOPPING_FOR_RELOAD",false)){SessionState.SetBool("SDK_E_STOPPING_FOR_RELOAD",true);EditorApplication.ExitPlaymode();}
   }else if(SessionState.GetBool("SDK_E_RELOAD_OCCURRED",false)){
    if(!reset){author.peak=0;author.low=0;author.frames=0;reset=true;File.WriteAllText(Path.Combine(output,"reloaded"),"afterAssemblyReload");}
    else if(author.frames>=10&&author.low>0){File.WriteAllText(Path.Combine(output,"reload-heard"),"fresh author updates heard audio");}
    if(File.Exists(Path.Combine(output,"finish"))){File.WriteAllText(Path.Combine(output,"play.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"framesAfterReload",author.frames},{"peakAfterReload",author.peak}}.ToString());SessionState.SetBool("SDK_E_AUDIO_RELOAD",false);EditorApplication.Exit(0);}
   }
  }
  if(EditorApplication.timeSinceStartup-began>30){SessionState.SetBool("SDK_E_AUDIO_RELOAD",false);Debug.LogError("Audio-reload-Play-timeout");EditorApplication.Exit(31);}
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Audio reload author").AddComponent<StageEAudioFailureAuthor>();SessionState.SetBool("SDK_E_STOPPING_FOR_RELOAD",false);SessionState.SetBool("SDK_E_REENTERED",false);SessionState.SetBool("SDK_E_RELOAD_REQUESTED",false);SessionState.SetBool("SDK_E_RELOAD_OCCURRED",false);SessionState.SetBool("SDK_E_AUDIO_RELOAD",true);EditorApplication.EnterPlaymode();}
}

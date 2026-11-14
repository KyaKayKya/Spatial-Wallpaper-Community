using System;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
[InitializeOnLoad]
public static class StageEAudioFailurePlay {
 static double began,zeroBegan;static int nonzeroFrames=-1;
 static StageEAudioFailurePlay(){EditorApplication.update+=Observe;}
 static void Observe(){
  if(!SessionState.GetBool("SDK_E_AUDIO_FAILURE",false))return;
  if(began==0)began=EditorApplication.timeSinceStartup;
  string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");var author=UnityEngine.Object.FindObjectOfType<StageEAudioFailureAuthor>();
  if(EditorApplication.isPlaying&&author){
   if(author.peak>0&&!File.Exists(Path.Combine(output,"heard")))File.WriteAllText(Path.Combine(output,"heard"),"ready");
   if(File.Exists(Path.Combine(output,"broker-killed"))){
    author.measure=true;
    if(nonzeroFrames!=author.nonzeroFrames||author.zeroFrames==0){nonzeroFrames=author.nonzeroFrames;zeroBegan=EditorApplication.timeSinceStartup;}
    double zeroSeconds=EditorApplication.timeSinceStartup-zeroBegan;
    if(author.zeroFrames>=10&&zeroSeconds>=2){File.WriteAllText(Path.Combine(output,"play.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"peak",author.peak},{"zeroFrames",author.zeroFrames},{"zeroSeconds",zeroSeconds},{"frames",author.frames}}.ToString());File.WriteAllText(Path.Combine(output,"cleared"),"four bands zero throughout two seconds of author updates");SessionState.SetBool("SDK_E_AUDIO_FAILURE",false);EditorApplication.Exit(0);}
   }
  }
  if(EditorApplication.timeSinceStartup-began>30){SessionState.SetBool("SDK_E_AUDIO_FAILURE",false);Debug.LogError("Audio-failure-Play-timeout");EditorApplication.Exit(31);}
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Audio failure author").AddComponent<StageEAudioFailureAuthor>();SessionState.SetBool("SDK_E_AUDIO_FAILURE",true);EditorApplication.EnterPlaymode();}
}

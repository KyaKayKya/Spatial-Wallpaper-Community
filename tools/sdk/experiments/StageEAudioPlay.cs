using System;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
[InitializeOnLoad]
public static class StageEAudioPlay {
 static double began;static StageEAudioPlay(){EditorApplication.update+=Observe;}
 static void Observe(){if(!SessionState.GetBool("SDK_E_AUDIO_PLAY",false))return;if(began==0)began=EditorApplication.timeSinceStartup;var author=UnityEngine.Object.FindObjectOfType<StageEAudioAuthor>();string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");
  if(EditorApplication.isPlaying&&author){if(author.phase==1&&author.heardPhase==3&&!File.Exists(Path.Combine(output,"heard")))File.WriteAllText(Path.Combine(output,"heard"),"ready");
   if(author.phase==4){File.WriteAllText(Path.Combine(output,"play.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"phase",author.phase},{"peak",author.peak}}.ToString());SessionState.SetBool("SDK_E_AUDIO_PLAY",false);EditorApplication.Exit(0);}}
  if(EditorApplication.timeSinceStartup-began>30){SessionState.SetBool("SDK_E_AUDIO_PLAY",false);Debug.LogError("Audio-Play-timeout");EditorApplication.Exit(31);}
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Audio Play author").AddComponent<StageEAudioAuthor>();SessionState.SetBool("SDK_E_AUDIO_PLAY",true);EditorApplication.EnterPlaymode();}
}

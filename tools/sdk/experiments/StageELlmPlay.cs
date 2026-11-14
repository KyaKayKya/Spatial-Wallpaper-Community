using System;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
[InitializeOnLoad]
public static class StageELlmPlay {
 static double began;static StageELlmPlay(){EditorApplication.update+=Observe;}
 static void Observe(){if(!SessionState.GetBool("SDK_E_LLM_PLAY",false))return;if(began==0)began=EditorApplication.timeSinceStartup;var author=UnityEngine.Object.FindObjectOfType<StageELlmAuthor>();
  if(EditorApplication.isPlaying&&author&&author.phase==8){if(author.overflow!=0||author.cancelRequest<1)throw new Exception("LLM-capacity-control-lane");foreach(int id in author.pending)if(id<1)throw new Exception("LLM-capacity-not-filled");File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("SDK_E_OUTPUT"),"play.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"phase",author.phase},{"result",author.result},{"cancellation",author.cancellation}}.ToString());SessionState.SetBool("SDK_E_LLM_PLAY",false);EditorApplication.Exit(0);}
  else if(EditorApplication.timeSinceStartup-began>25){SessionState.SetBool("SDK_E_LLM_PLAY",false);Debug.LogError("LLM-Play-timeout");EditorApplication.Exit(31);}
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("LLM Play author").AddComponent<StageELlmAuthor>();SessionState.SetBool("SDK_E_LLM_PLAY",true);EditorApplication.EnterPlaymode();}
}

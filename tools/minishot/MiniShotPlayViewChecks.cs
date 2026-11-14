using System;using System.IO;using UnityEditor;using UnityEngine;using SpatialWallpaper.Sdk;
[InitializeOnLoad]public static class MiniShotPlayViewChecks {
 const string Key="MiniShot.PlayView.Check";
 static MiniShotPlayViewChecks(){EditorApplication.update+=Poll;}
 public static void Run(){SessionState.SetString(Key+".out",Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT"));try{MiniShotSdkBuilder.Create();UnityEngine.Object.DestroyImmediate(UnityEngine.Object.FindObjectOfType<MiniShotSandbox>());SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);SessionState.SetInt(Key,1);Debug.Log("PLAYVIEW entering Play");EditorApplication.isPlaying=true;}catch(Exception e){Fail(e);}}
 static void Check(){var p=UnityEngine.Object.FindObjectOfType<WindowProjection>();if(!p||View.GetEye()!=p.EyeScreen||View.GetSize()!=new Vector3(p.displayWidth,p.displayHeight,0))throw new Exception("ordinary-play-view-binding-mismatch");}
 static void Poll(){int stage=SessionState.GetInt(Key,0);if(stage==0)return;try{
  if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0)>90)throw new TimeoutException("Play View lifecycle stage "+stage);
  if(stage==1&&EditorApplication.isPlaying){Check();SessionState.SetInt(Key,3);Debug.Log("PLAYVIEW bound; exiting Play");EditorApplication.isPlaying=false;}
  else if(stage==3&&!EditorApplication.isPlaying){bool refused=false;try{View.GetSize();}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("stopped-ordinary-view-still-bound");File.WriteAllText(Path.Combine(SessionState.GetString(Key+".out",""),"result.json"),"{\"status\":\"passed-ordinary-Play-view-enter-exit\",\"active_recompile\":\"unverified; restart Play after code changes\"}");SessionState.EraseInt(Key);EditorApplication.Exit(0);}
 }catch(Exception e){Fail(e);}}
 static void Fail(Exception e){File.WriteAllText(Path.Combine(SessionState.GetString(Key+".out",""),"error.txt"),e.ToString());SessionState.EraseInt(Key);Debug.LogException(e);EditorApplication.Exit(1);}
}

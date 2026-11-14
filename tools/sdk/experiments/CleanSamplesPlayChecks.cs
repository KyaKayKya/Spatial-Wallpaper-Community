using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CleanSamplesPlayChecks {
 const string Active="SDK_G_CLEAN_PLAY",Stopping="SDK_G_CLEAN_PLAY_STOPPING";
 static double began;
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll")]static extern bool GetExitCodeProcess(IntPtr handle,out uint exitCode);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 static IntPtr Retained=>new IntPtr(long.Parse(SessionState.GetString("SDK_G_PLAY_HANDLE","0")));
 static string Output=>Environment.GetEnvironmentVariable("SDK_G_OUTPUT");
 static bool AudioFixture=>Environment.GetEnvironmentVariable("SDK_G_AUDIO_FIXTURE")=="1";
 static CleanSamplesPlayChecks(){EditorApplication.update+=Observe;}
 static void Finish(Exception error){SessionState.SetBool(Active,false);if(error!=null){File.WriteAllText(Path.Combine(Output,"error.txt"),error.ToString());UnityEngine.Debug.LogException(error);}if(Retained!=IntPtr.Zero)CloseHandle(Retained);SessionState.SetString("SDK_G_PLAY_HANDLE","0");EditorApplication.Exit(error==null?0:1);}
 static void Observe(){if(!SessionState.GetBool(Active,false))return;try{
  if(began==0)began=EditorApplication.timeSinceStartup;
  if(EditorApplication.timeSinceStartup-began>25)throw new TimeoutException("installed-sdk-ordinary-play-deadline");
  if(SessionState.GetBool(Stopping,false)){
   if(EditorApplication.isPlayingOrWillChangePlaymode)return;
   if(Retained==IntPtr.Zero)throw new Exception("installed-play-retained-kernel-missing");if(WaitForSingleObject(Retained,0)!=0)return;
   if(!GetExitCodeProcess(Retained,out uint code)||code!=0)throw new Exception("installed-play-broker-nonzero-exit:"+code);
   File.WriteAllText(Path.Combine(Output,"result.json"),new JObject{{"ordinary_play",true},{"reply",AudioFixture?null:"reply-as-data"},{"reply_stored",!AudioFixture},{"audio_wiring_only",AudioFixture},{"broker_exited_zero",true},{"scope",AudioFixture?"copied installed SDK, default bundled broker and project-local selected audio source; test author heard and subsequent zero-band observations, normal Play stop; not continuous-silence or audio long-run acceptance":"copied installed SDK, default bundled broker path, ordinary Play Start/Update LLM simulation and storage, normal Play stop; not domain reload or external model verification"}}.ToString());Finish(null);return;
  }
  if(!EditorApplication.isPlaying)return;
  if(AudioFixture){var audio=UnityEngine.Object.FindObjectOfType<AudioSample>();if(!audio)return;var phase=(int)audio.GetType().GetField("phase").GetValue(audio);if(phase>=3&&!File.Exists(Path.Combine(Output,"audio-heard")))File.WriteAllText(Path.Combine(Output,"audio-heard"),"current ordinary Play author heard selected source");if(phase!=6)return;}
  else{var author=UnityEngine.Object.FindObjectOfType<LlmReplySample>();if(!author)return;if(author.phase==4)throw new Exception("installed-play-author-failed:"+author.error);if(author.phase!=5)return;if(author.reply!="reply-as-data")throw new Exception("installed-play-not-deterministic-reply");}
  var type=typeof(SpatialWallpaper.Sdk.Editor.ScriptSceneExporter).Assembly.GetType("SpatialWallpaper.Sdk.Editor.PlayCapabilities");var broker=(Process)type.GetField("broker",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  if(broker==null||broker.HasExited)throw new Exception("installed-play-broker-not-running");
  // Retain the actual process kernel across Unity's ordinary AppDomain reload.
  var handle=OpenProcess(0x101000,false,broker.Id);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());SessionState.SetString("SDK_G_PLAY_HANDLE",handle.ToInt64().ToString());SessionState.SetBool(Stopping,true);EditorApplication.ExitPlaymode();
 }catch(Exception error){Finish(error);}}
 public static void Run(){try{
  var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SpatialWallpaper.Sdk.Editor.ScriptSceneExporter).Assembly);
  if(!File.Exists(Path.Combine(package.resolvedPath,"Editor/Windows~/PlayBroker.exe")))throw new FileNotFoundException("installed-play-broker-missing");
  PlayerSettings.colorSpace=ColorSpace.Linear;SdkSamples.Create(AudioFixture?"Audio":"LlmReply");SessionState.SetBool(Stopping,false);SessionState.SetBool(Active,true);EditorApplication.EnterPlaymode();
 }catch(Exception error){Finish(error);}}
}

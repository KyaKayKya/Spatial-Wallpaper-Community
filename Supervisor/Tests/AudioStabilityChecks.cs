using System;using System.Collections.Generic;using System.Reflection;
namespace UnityEngine { public class MonoBehaviour {} }
namespace SpatialWallpaper.Sdk {
 [AttributeUsage(AttributeTargets.Class)]public sealed class WallpaperScriptAttribute:Attribute {}
 public static class Audio { public static float[] Values=new float[4];public static float GetBand(int band)=>Values[band]; }
 public static class Storage {
  public static Dictionary<string,string> Saved=new Dictionary<string,string>();public static int Writes;
  public static int BeginRead(string key)=>1;public static int State(int request)=>3;public static string Version(int request)=>"test-version";public static void Release(int request){}
  public static int BeginWriteText(string key,string value,string version){Saved[key]=value;Writes++;return 1;}
 }
}
static class AudioStabilityChecks {
 static void Run(int band,int update,bool enabled=true){
  SpatialWallpaper.Sdk.Storage.Saved.Clear();SpatialWallpaper.Sdk.Storage.Writes=0;SpatialWallpaper.Sdk.Audio.Values=new float[4];
  var author=new StageEAudioAuthor{phase=4,heardPhase=3,requireStableZero=enabled};var tick=typeof(StageEAudioAuthor).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
  for(int i=0;i<126;i++){Array.Clear(SpatialWallpaper.Sdk.Audio.Values,0,4);if(i==update)SpatialWallpaper.Sdk.Audio.Values[band]=.5f;tick.Invoke(author,null);}
  if(!enabled){if(SpatialWallpaper.Sdk.Storage.Writes!=0||author.stableUpdates!=0)throw new Exception("stability-opt-in");return;}
  string expected=band<0?"120-updates-zero":"nonzero-returned";
  if(author.stableUpdates!=120||author.stablePhase!=3||SpatialWallpaper.Sdk.Storage.Writes!=1||SpatialWallpaper.Sdk.Storage.Saved["audio-stability"]!=expected)throw new Exception("stability-latch-band-"+band+"-update-"+update);
 }
 static int Main(){try{Run(-1,-1);Run(-1,-1,false);for(int band=0;band<4;band++)foreach(int update in new[]{0,59,119})Run(band,update);Console.WriteLine("PASS 14 actual-author controlled replay cases; not native audio or VM evidence");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

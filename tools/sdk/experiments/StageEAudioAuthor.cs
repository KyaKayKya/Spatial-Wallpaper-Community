using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEAudioAuthor:MonoBehaviour {
 public int phase,request,heardPhase,heardRequest;public float peak;public string version,heardVersion;
 public int stablePhase,stableUpdates,stableRequest;public bool requireStableZero,returnedAudio;public string stableVersion;
 void Update(){
  float low=SpatialWallpaper.Sdk.Audio.GetBand(0);
  if(phase==0&&low!=0){peak=low;phase=1;heardRequest=Storage.BeginRead("audio-heard");heardPhase=1;}
  else if(phase==1&&low==0&&heardPhase==3){request=Storage.BeginRead("audio-result");phase=2;}
  else if(phase==2&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("audio-result","audio-cleared",version);phase=3;}
  else if(phase==3&&Storage.State(request)==3){Storage.Release(request);phase=4;}
  if(heardPhase==1&&Storage.State(heardRequest)==3){heardVersion=Storage.Version(heardRequest);Storage.Release(heardRequest);heardRequest=Storage.BeginWriteText("audio-heard","audio-heard",heardVersion);heardPhase=2;}
  else if(heardPhase==2&&Storage.State(heardRequest)==3){Storage.Release(heardRequest);heardPhase=3;}
  // Separate post-clear evidence; ordinary Play may stop at phase 4 while
  // the Host stability fixture requires this third durable revision.
  if(requireStableZero&&phase==4&&stablePhase==0){
   if(SpatialWallpaper.Sdk.Audio.GetBand(0)!=0||SpatialWallpaper.Sdk.Audio.GetBand(1)!=0||SpatialWallpaper.Sdk.Audio.GetBand(2)!=0||SpatialWallpaper.Sdk.Audio.GetBand(3)!=0)returnedAudio=true;
   stableUpdates=stableUpdates+1;
   if(stableUpdates>=120){stableRequest=Storage.BeginRead("audio-stability");stablePhase=1;}
  }
  else if(stablePhase==1&&Storage.State(stableRequest)==3){stableVersion=Storage.Version(stableRequest);Storage.Release(stableRequest);string result="120-updates-zero";if(returnedAudio)result="nonzero-returned";stableRequest=Storage.BeginWriteText("audio-stability",result,stableVersion);stablePhase=2;}
  else if(stablePhase==2&&Storage.State(stableRequest)==3){Storage.Release(stableRequest);stablePhase=3;}
 }
}

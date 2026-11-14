// Original MIT SDK capability verification author; no platform or file API.
using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEStorageAuthor:MonoBehaviour {
 public int phase;public int request;public string version;public string result;
 void Start(){request=Storage.BeginRead("alpha");phase=1;}
 void Update(){
  if(request==0||Storage.State(request)!=3)return;
  if(phase==1){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("alpha","author-written",version);phase=2;}
  else if(phase==2){Storage.Release(request);request=Storage.BeginRead("alpha");phase=3;}
  else if(phase==3){version=Storage.Version(request);result=Storage.TakeText(request);request=Storage.BeginWriteText("beta",result,version);phase=4;}
  else if(phase==4){Storage.Release(request);request=0;phase=5;}
 }
}

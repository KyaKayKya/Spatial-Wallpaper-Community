using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEStorageMatrixAuthor:MonoBehaviour {
 public int role;public int phase;public int request;public int retries;public string outcome;public string version;public string value;public string failure;
 void Start(){if(role<3){request=Storage.BeginWriteText("winner",role==1?"A":"B","0");phase=1;}else{request=Storage.BeginRead("winner");phase=3;}}
 void Update(){
  if(request==0)return;int state=Storage.State(request);if(state!=3&&state!=4)return;
  if(phase==80){if(state==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("author-error",failure,version);phase=81;}else{Storage.Release(request);request=Storage.BeginRead("winner");}return;}
  if(phase==81){Storage.Release(request);if(state==3){request=0;phase=-1;}else{request=Storage.BeginRead("winner");phase=80;}return;}
  if(state==4&&phase==4&&Storage.Error(request)=="Conflict"&&retries<4){retries=retries+1;Storage.Release(request);request=Storage.BeginRead("winner");phase=2;return;}
  if(state==4&&phase!=1){failure=Storage.Error(request);Storage.Release(request);request=Storage.BeginRead("winner");phase=80;return;}
  if(phase==1){outcome=state==3?"success":Storage.Error(request);Storage.Release(request);request=Storage.BeginRead("winner");phase=2;}
  else if(phase==2){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText(role==1?"resultA":"resultB",outcome,version);phase=4;}
  else if(phase==3){version=Storage.Version(request);value=Storage.TakeText(request);request=Storage.BeginWriteText("observed",value,version);phase=5;}
  else if(phase==4){Storage.Release(request);request=0;phase=6;}
  else if(phase==5){outcome=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("read-version",version,outcome);phase=7;}
  else if(phase==7){Storage.Release(request);request=0;phase=6;}
 }
}

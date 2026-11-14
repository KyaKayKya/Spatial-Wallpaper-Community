using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEInputRuntimeAuthor:MonoBehaviour {
 public int phase,request,sideEffects;public string version;public bool sawRelease,sawPress,sawWheel;
 bool SideEffect(){sideEffects=sideEffects+1;return true;}
 void Update(){
  bool held=SpatialWallpaper.Sdk.Input.GetButton("Space"),down=SpatialWallpaper.Sdk.Input.GetButtonDown("Space"),up=SpatialWallpaper.Sdk.Input.GetButtonUp("Space"),focused=SpatialWallpaper.Sdk.Input.IsFocused();
  float x=SpatialWallpaper.Sdk.Input.GetPointerX(),y=SpatialWallpaper.Sdk.Input.GetPointerY(),wheel=SpatialWallpaper.Sdk.Input.GetWheel();
  if(up){if(!held){if(focused){if(!down){if(SpatialWallpaper.Sdk.Input.GetButtonUp("Space"))sawRelease=true;}}}}
  if(phase==0){
   // The native events may arrive in adjacent author frames. Accumulate only
   // while this press remains held and focused; repeated reads must be stable.
   if(!focused||!held||up){sawPress=false;sawWheel=false;}
   if(held&&focused&&!up&&x>.24f&&x<.26f&&y>.74f&&y<.76f&&x>=.24f&&y<=.76f){
    if(down&&SpatialWallpaper.Sdk.Input.GetButtonDown("Space"))sawPress=true;
    if(wheel==1&&SpatialWallpaper.Sdk.Input.GetWheel()==wheel)sawWheel=true;
   }
   if(sawPress&&sawWheel){
    if(!held&&SideEffect())return;
    if(held||SideEffect()){if(sideEffects!=0)return;sawRelease=false;request=Storage.BeginRead("input-held");phase=1;}
   }
  }
  else if(phase==1&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("input-held","observed",version);phase=2;}
  else if(phase==2&&Storage.State(request)==3){Storage.Release(request);phase=3;}
  else if(phase==3){if(sawRelease){request=Storage.BeginRead("input-released");phase=4;}}
  else if(phase==4&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("input-released","observed",version);phase=5;}
  else if(phase==5&&Storage.State(request)==3){Storage.Release(request);phase=6;}
  else if(phase==6){if(focused)return;if(held)return;if(down)return;if(up)return;if(x!=0)return;if(y!=0)return;if(wheel!=0)return;request=Storage.BeginRead("input-cleared");phase=7;}
  else if(phase==7&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("input-cleared","observed",version);phase=8;}
  else if(phase==8&&Storage.State(request)==3){Storage.Release(request);phase=9;}
 }
}

using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class InputStorageSample:MonoBehaviour {
 public Material surface;
 public int phase,request;
 public string saved,version,error;
 void Start(){request=Storage.BeginRead("starter-color");phase=1;}
 void Update(){
  if(phase==1&&Storage.State(request)==3){version=Storage.Version(request);saved=Storage.TakeText(request);request=0;phase=2;Paint();}
  if(phase==3&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=0;phase=2;}
  if(request>0&&Storage.State(request)==4){error=Storage.Error(request);Storage.Release(request);request=0;phase=4;}
  if(SpatialWallpaper.Sdk.Input.GetButtonDown("Space")){
   if(phase==4){request=Storage.BeginRead("starter-color");phase=1;}
   else if(phase==2){if(saved=="amber")saved="blue";else saved="amber";Paint();request=Storage.BeginWriteText("starter-color",saved,version);phase=3;}
  }
 }
 void Paint(){if(saved=="amber")surface.SetColor("_BaseColor",new Color(1,.5f,.08f,1));else surface.SetColor("_BaseColor",new Color(.08f,.4f,1,1));}
}

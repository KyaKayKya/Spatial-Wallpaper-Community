using UnityEngine;using SpatialWallpaper.Sdk;
// Compile into the isolated native-script program. Never load this author class in the host.
[WallpaperScript]
public sealed class AsyncViewStorage:MonoBehaviour {
 public Transform marker;public int request,phase;public string version,error;
 void Start(){request=Storage.BeginRead("example.visited");phase=1;}
 void Update(){
  var eye=View.GetEye();var size=View.GetSize();
  marker.localPosition=new Vector3(size.x*.4f,size.y*.4f,eye.z*.05f);
  if(request>0&&Storage.State(request)>=4){error=Storage.Error(request);Storage.Release(request);request=0;phase=4;}
  if(request>0&&Storage.State(request)==3){
   version=Storage.Version(request);
   if(phase==1){string previous=Storage.TakeText(request);request=0;phase=2;}
   else{Storage.Release(request);request=0;phase=2;}
  }
  if(phase==2&&SpatialWallpaper.Sdk.Input.IsFocused()&&SpatialWallpaper.Sdk.Input.GetButtonDown("Primary")){
   request=Storage.BeginWriteText("example.visited","yes",version);phase=3;
  }
 }
}

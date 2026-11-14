using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class LlmReplySample:MonoBehaviour {
 public Material surface;
 public int request,cancellation,storage,phase;
 public string reply,error,version;
 void Start(){request=Llm.BeginPrompt("starter scene response");phase=1;}
 void Update(){
  if(SpatialWallpaper.Sdk.Input.GetButtonDown("Space")){
   if(request>0&&cancellation==0)cancellation=Llm.BeginCancel(request);
   else if(request==0&&storage==0){request=Llm.BeginPrompt("starter scene response");phase=1;}
  }
  if(cancellation>0&&(Llm.State(cancellation)==3||Llm.State(cancellation)==4)){Llm.Release(cancellation);cancellation=0;}
  if(request>0&&Llm.State(request)==3){reply=Llm.TakeText(request);request=0;surface.SetColor("_BaseColor",new Color(.1f,.8f,.3f,1));storage=Storage.BeginRead("starter-llm-reply");phase=2;}
  if(request>0&&Llm.State(request)==4){error=Llm.Error(request);Llm.Release(request);request=0;phase=4;surface.SetColor("_BaseColor",new Color(1,.5f,.1f,1));}
  if(storage>0&&Storage.State(storage)==3){
   if(phase==2){version=Storage.Version(storage);Storage.Release(storage);storage=Storage.BeginWriteText("starter-llm-reply",reply,version);phase=3;}
   else{Storage.Release(storage);storage=0;phase=5;}
  }
  if(storage>0&&Storage.State(storage)==4){error=Storage.Error(storage);Storage.Release(storage);storage=0;phase=4;}
 }
}

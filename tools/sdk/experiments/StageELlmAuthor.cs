using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageELlmAuthor:MonoBehaviour {
 public int phase,request,cancelRequest,overflow;public int[] pending;public string result,cancellation,version;
 void Start(){request=Llm.BeginPrompt("scene response");phase=1;}
 void Update(){
  if(phase==1&&Llm.State(request)==3){result=Llm.TakeText(request);pending=new int[8];for(int i=0;i<8;i++)pending[i]=Llm.BeginPrompt("hold");request=pending[0];overflow=Llm.BeginPrompt("overflow");phase=2;}
  else if(phase==2){if(overflow!=0){phase=-1;return;}for(int i=0;i<8;i++)if(pending[i]==0){phase=-2;return;}cancelRequest=Llm.BeginCancel(request);if(cancelRequest>0)phase=3;}
  else if(phase==3&&Llm.State(cancelRequest)==3){Llm.Release(cancelRequest);phase=4;}
  else if(phase==4&&Llm.State(request)==4){cancellation=Llm.Error(request);Llm.Release(request);request=Storage.BeginRead("llm-result");phase=5;}
  else if(phase==5&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("llm-result",result,version);phase=6;}
  else if(phase==6&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("llm-cancel",cancellation,version);phase=7;}
  else if(phase==7&&Storage.State(request)==3){Storage.Release(request);request=0;phase=8;}
 }
}

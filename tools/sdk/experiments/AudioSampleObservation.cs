using UnityEngine;using SpatialWallpaper.Sdk;
// Test-only author: same band-driven material plus durable heard/quiet markers.
[WallpaperScript]public sealed class AudioSample:MonoBehaviour {
 public Material surface;public float lowBand;public int phase,request;public string version;
 void Update(){lowBand=SpatialWallpaper.Sdk.Audio.GetBand(0);surface.SetColor("_BaseColor",new Color(.1f+lowBand*.8f,.2f+lowBand*.5f,.6f,1));
  if(phase==0&&lowBand>0){request=Storage.BeginRead("audio-selected-heard");phase=1;}
  if(phase==1&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("audio-selected-heard","heard",version);phase=2;}
  if(phase==2&&Storage.State(request)==3){Storage.Release(request);request=0;phase=3;}
  if(phase==3&&lowBand==0){request=Storage.BeginRead("audio-selected-quiet");phase=4;}
  if(phase==4&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("audio-selected-quiet","quiet",version);phase=5;}
  if(phase==5&&Storage.State(request)==3){Storage.Release(request);request=0;phase=6;}
 }
}

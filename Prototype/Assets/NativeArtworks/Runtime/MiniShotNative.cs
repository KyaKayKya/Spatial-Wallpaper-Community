using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class MiniShotNative:MonoBehaviour {
 public Vector3[] centers;
 public float[] radii;
 public GameObject[] targets,trails;
 
 public AudioClip music,hitSound,resetSound;
 
 public float windowWidth,windowHeight;
 public string[] keys;
 public bool[] hits,dirty;
 public int phase,request,loading,shown=-1;
 public string version,error;

 void Start(){Sound.PlayMusic(music,1);phase=1;loading=0;request=Storage.BeginRead(keys[0]);}
 void Update(){
  
  if(request>0&&Storage.State(request)>=4){error=Storage.Error(request);Storage.Release(request);request=0;phase=4;}
  if(phase==1&&Storage.State(request)==3){
   version=Storage.Version(request);string value=Storage.TakeText(request);request=0;
   if(value!=null&&value!="hit"){error="unknown-saved-target-state";phase=4;}
   else{hits[loading]=value=="hit";dirty[loading]=false;Paint(loading);loading=loading+1;if(loading<keys.Length)request=Storage.BeginRead(keys[loading]);else phase=2;}
  }
  if(phase==3&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=0;phase=2;}
  // Only dirty values from a preceding acknowledged scene Tick may persist.
  if(phase==2){for(int i=0;i<dirty.Length;i++)if(dirty[i]){dirty[i]=false;request=Storage.BeginWriteText(keys[i],hits[i]?"hit":null,version);phase=3;break;}}
  bool focused=SpatialWallpaper.Sdk.Input.IsFocused();
  if(focused){
   if(phase==4&&SpatialWallpaper.Sdk.Input.GetButtonDown("Space")){error=null;loading=0;phase=1;request=Storage.BeginRead(keys[0]);}
   else if(phase==2||phase==3){
    if(SpatialWallpaper.Sdk.Input.GetButtonDown("Space")){Sound.PlayEffect(resetSound,.65f);for(int i=0;i<hits.Length;i++){hits[i]=false;dirty[i]=true;Paint(i);}}
    else if(SpatialWallpaper.Sdk.Input.GetButtonDown("Primary")){
     int index=Pick(SpatialWallpaper.Sdk.Input.GetPointerX(),SpatialWallpaper.Sdk.Input.GetPointerY(),View.GetEye(),View.GetSize());
     if(index>=0&&!hits[index]){Sound.PlayEffect(hitSound,.7f);hits[index]=true;dirty[index]=true;Paint(index);}
    }
   }
  }
  if(request==0&&(phase==1||phase==3)){error="storage-request-not-started";phase=4;}

 }
 void Paint(int index){targets[index].SetActive(!hits[index]);trails[index].SetActive(!hits[index]);}
 public int Pick(float x,float y,Vector3 eye,Vector3 size){
  float sx=(x-.5f)*size.x,sy=(.5f-y)*size.y;float nearest=100000;int selected=-1;
  for(int i=0;i<centers.Length;i++){
   var center=centers[i];float t=(center.z-eye.z)/(-eye.z);
   float dx=eye.x+(sx-eye.x)*t-center.x,dy=eye.y+(sy-eye.y)*t-center.y;
   if(t>0&&t<nearest&&dx*dx+dy*dy<=radii[i]*radii[i]){nearest=t;selected=i;}
  }
  return selected;
 }
}

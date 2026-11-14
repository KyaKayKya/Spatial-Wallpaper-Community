using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class MiniShotSandbox:MonoBehaviour {
 public Vector3[] centers;
 public float[] radii;
 public Material[] targets;
 public Material status;
 public AudioClip music,hitSound,resetSound;
 public Transform indicator;
 public float windowWidth,windowHeight;
 public string[] keys;
 public bool[] hits,dirty;
 public int phase,request,loading,shown=-1;
 public string version,error;

 void Start(){Sound.PlayMusic(music,1);phase=1;loading=0;request=Storage.BeginRead(keys[0]);}
 void Update(){
  var window=View.GetSize();if(windowWidth!=window.x||windowHeight!=window.y){windowWidth=window.x;windowHeight=window.y;indicator.localPosition=new Vector3(window.x*.5f-.008f,window.y*.5f-.008f,0);}
  if(request>0&&Storage.State(request)>=4){error=Storage.Error(request);Storage.Release(request);request=0;phase=4;}
  if(phase==1&&Storage.State(request)==3){
   version=Storage.Version(request);string value=Storage.TakeText(request);request=0;
   if(value!=null&&value!="hit"){error="unknown-saved-target-state";phase=4;}
   else{hits[loading]=value=="hit";dirty[loading]=false;Paint(loading);loading=loading+1;if(loading<keys.Length)request=Storage.BeginRead(keys[loading]);else phase=2;}
  }
  if(phase==3&&Storage.State(request)==3){version=Storage.Version(request);Storage.Release(request);request=0;phase=2;}
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
  if(phase==2){for(int i=0;i<dirty.Length;i++)if(dirty[i]){dirty[i]=false;request=Storage.BeginWriteText(keys[i],hits[i]?"hit":null,version);phase=3;break;}}
  if(request==0&&(phase==1||phase==3)){error="storage-request-not-started";phase=4;}
  int display=phase*2;if(focused)display=display+1;
  if(shown!=display){shown=display;if(phase==4)status.SetColor("_BaseColor",new Color(1,.06f,.03f,1));else if(phase==2&&focused)status.SetColor("_BaseColor",new Color(.1f,.35f,1,1));else if(phase==2)status.SetColor("_BaseColor",new Color(.12f,.6f,.4f,1));else status.SetColor("_BaseColor",new Color(.85f,.4f,.04f,1));}
 }
 void Paint(int index){targets[index].SetFloat("_Hit",hits[index]?1:0);}
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

using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEInputContinuityAuthor:MonoBehaviour {
 public int step,edges,cycles,request,io,heldMask;public bool armed;public string queued,current,version,failure;
 string Name(int index){if(index==0)return "Space";if(index==1)return "Left";if(index==2)return "Right";if(index==3)return "Up";if(index==4)return "Down";if(index==5)return "Primary";return "Secondary";}
 string Ack(int s){if(s==0)return "Space-down";if(s==1)return "Space-up";if(s==2)return "Left-down";if(s==3)return "Left-up";if(s==4)return "Right-down";if(s==5)return "Right-up";if(s==6)return "Up-down";if(s==7)return "Up-up";if(s==8)return "Down-down";if(s==9)return "Down-up";if(s==10)return "Primary-down";if(s==11)return "Primary-up";if(s==12)return "Secondary-down";return "Secondary-up";}
 void Update(){
  // Waiting for the initial trusted focus is allowed; loss after arming is terminal.
  bool focused=SpatialWallpaper.Sdk.Input.IsFocused();
  if(focused!=SpatialWallpaper.Sdk.Input.IsFocused()){failure="unstable-focus";}
  if(!focused){if(armed)failure="focus-lost";return;}armed=true;
  int held=0,down=0,up=0,bit=1;
  for(int i=0;i<7;i++){
   string key=Name(i);bool h=SpatialWallpaper.Sdk.Input.GetButton(key),d=SpatialWallpaper.Sdk.Input.GetButtonDown(key),u=SpatialWallpaper.Sdk.Input.GetButtonUp(key);
   if(h!=SpatialWallpaper.Sdk.Input.GetButton(key)||d!=SpatialWallpaper.Sdk.Input.GetButtonDown(key)||u!=SpatialWallpaper.Sdk.Input.GetButtonUp(key))failure="unstable-buttons";
   if(h)held=held|bit;if(d)down=down|bit;if(u)up=up|bit;bit=bit*2;
  }
  float x=SpatialWallpaper.Sdk.Input.GetPointerX(),y=SpatialWallpaper.Sdk.Input.GetPointerY(),w=SpatialWallpaper.Sdk.Input.GetWheel();
  if(x!=SpatialWallpaper.Sdk.Input.GetPointerX()||y!=SpatialWallpaper.Sdk.Input.GetPointerY()||w!=SpatialWallpaper.Sdk.Input.GetWheel())failure="unstable-pointer";
  if(x<0||x>1||y<0||y>1||w!=0)failure="unexpected-pointer-or-wheel";
  if(failure!=null)return;
  if(down!=0||up!=0){
   int expected=1;for(int i=0;i<step/2;i++)expected=expected*2;
   bool press=step%2==0;
   if((press&&(down!=expected||up!=0||held!=expected||heldMask!=0))||(!press&&(up!=expected||down!=0||held!=0||heldMask!=expected))){failure="unexpected-edge";return;}
   if(queued!=null){failure="edge-queue-overflow";return;}
   queued=Ack(step);heldMask=held;edges=edges+1;step=step+1;if(step==14){step=0;cycles=cycles+1;}
  }else if(held!=heldMask){failure="missing-edge";return;}
  // One in-flight durable write plus one next edge. A commit can be visible to
  // the driver before this author observes its terminal reply.
  if(request!=0){
   int state=Storage.State(request);if(state==4||state==5){failure="storage-failed";Storage.Release(request);request=0;return;}
   if(state!=3)return;
   if(io==1){version=Storage.Version(request);Storage.Release(request);request=Storage.BeginWriteText("continuity-ack",current,version);io=2;}
   else{Storage.Release(request);request=0;io=0;current=null;}
   if(io==2&&request==0)failure="storage-begin-rejected";
  }
  if(request==0&&queued!=null){current=queued;queued=null;request=Storage.BeginRead("continuity-ack");io=1;if(request==0)failure="storage-begin-rejected";}
 }
}

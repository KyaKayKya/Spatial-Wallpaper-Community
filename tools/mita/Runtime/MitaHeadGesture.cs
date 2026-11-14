using System;
using System.Collections.Generic;

namespace SpatialWallpaper.Mita {
 public enum MitaHeadGesture { None,Nod,Shake }

 // Head rotation only. Translation and projection calibration never enter recognition.
 public sealed class MitaHeadGestureDetector {
  bool initialized,armed;
  float centerPitch,centerYaw,lastPitch,lastYaw,lastAt,stableSince,started,releaseAt;
  int phase,side;
  static float Delta(float a,float b){float v=(a-b)%360;return v>180?v-360:v< -180?v+360:v;}
  public void Reset(){initialized=armed=false;phase=0;}
  public MitaHeadGesture Observe(float now,float pitch,float yaw,bool valid){
   if(!valid){Reset();return MitaHeadGesture.None;}
   if(!initialized||now-lastAt>.3f||now<lastAt){
    initialized=true;armed=false;phase=0;centerPitch=lastPitch=pitch;centerYaw=lastYaw=yaw;stableSince=lastAt=now;return MitaHeadGesture.None;
   }
   float movement=Math.Abs(Delta(pitch,lastPitch))+Math.Abs(Delta(yaw,lastYaw));
   lastPitch=pitch;lastYaw=yaw;lastAt=now;
   if(!armed){
    if(movement>1){stableSince=now;centerPitch=pitch;centerYaw=yaw;}
    if(now-stableSince>=.35f){armed=true;centerPitch=pitch;centerYaw=yaw;}
    return MitaHeadGesture.None;
   }
   float p=Delta(pitch,centerPitch),y=Delta(yaw,centerYaw);
   if(phase!=0&&now-started>1.4f){armed=false;phase=0;stableSince=now;return MitaHeadGesture.None;}
   if(now<releaseAt)return MitaHeadGesture.None;
   if(phase==0){
    if(Math.Abs(p)>=9&&Math.Abs(y)<6){phase=1;started=now;}
    else if(Math.Abs(y)>=11&&Math.Abs(p)<6){phase=2;side=Math.Sign(y);started=now;}
    else if(Math.Abs(p)<3&&Math.Abs(y)<3&&movement<.3f){centerPitch+=p*.01f;centerYaw+=y*.01f;}
   }else if(phase==1){
    if(Math.Abs(y)>=8){phase=0;return MitaHeadGesture.None;}
    if(Math.Abs(p)<=3&&now-started>=.12f){phase=0;releaseAt=now+.18f;return MitaHeadGesture.Nod;}
   }else{
    if(Math.Abs(p)>=8){phase=0;return MitaHeadGesture.None;}
    if(phase==2&&y*side<=-11)phase=3;
    if(phase==3&&Math.Abs(y)<=4&&now-started>=.2f){phase=0;releaseAt=now+.18f;return MitaHeadGesture.Shake;}
   }
   return MitaHeadGesture.None;
  }
 }

 // Every accepted event gets exactly one start. A bounded burst cannot leave a long backlog.
 public sealed class MitaResponseQueue {
  readonly Queue<MitaHeadGesture> pending=new Queue<MitaHeadGesture>();
  int accepted;
  float lastAccepted,cooldownUntil;
  bool playing;
  public int Pending=>pending.Count;
  public bool Playing=>playing;
  public float CooldownUntil=>cooldownUntil;
  void Tick(float now){
   if(cooldownUntil>0&&now>=cooldownUntil){cooldownUntil=0;accepted=0;}
   if(cooldownUntil==0&&accepted>0&&!playing&&pending.Count==0&&now-lastAccepted>=1)cooldownUntil=now+3;
  }
  public bool Accept(float now,MitaHeadGesture gesture){
   Tick(now);
   if(gesture==MitaHeadGesture.None||cooldownUntil>0||accepted>=3)return false;
   pending.Enqueue(gesture);accepted++;lastAccepted=now;return true;
  }
  public MitaHeadGesture StartNext(float now){
   Tick(now);
   if(playing||pending.Count==0||now-lastAccepted<.18f)return MitaHeadGesture.None;
   playing=true;return pending.Dequeue();
  }
  public void Complete(float now){
   playing=false;
   if(pending.Count==0&&accepted>=3)cooldownUntil=now+3;
  }
  public void Reset(){pending.Clear();accepted=0;playing=false;cooldownUntil=0;}
 }
}

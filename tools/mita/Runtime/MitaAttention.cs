using System;

namespace SpatialWallpaper.Mita {
 public enum MitaAttentionSource { Native,Click,Mouse,User }

 // Scaled time is supplied by the caller so pause freezes every attention timer.
 public sealed class MitaAttention {
  readonly Random random;
  float clickUntil,mouseUntil,userUntil,stableSince;
  bool wasFresh,presencePending;
  public float NextGlanceAt {get;private set;}
  public MitaAttentionSource Source {get;private set;}
  public MitaAttention(int seed){random=new Random(seed);Reset(0);}
  float Interval()=>10+(float)random.NextDouble()*30;
  public void Reset(float now){
   clickUntil=mouseUntil=userUntil=0;stableSince=-1;wasFresh=presencePending=false;
   NextGlanceAt=now+Interval();Source=MitaAttentionSource.Native;
  }
  public void Click(float now){clickUntil=now+4f;userUntil=0;presencePending=false;NextGlanceAt=clickUntil+Interval();}
  public MitaAttentionSource Step(float now,bool available,bool busy,bool fresh,bool nearby,bool moved){
   if(!available||busy){Reset(now);return Source;}
   if(fresh&&!wasFresh){stableSince=now;presencePending=true;}
   if(!fresh){stableSince=-1;presencePending=false;userUntil=0;}
   wasFresh=fresh;
   if(nearby&&moved)mouseUntil=now+1.2f;
   if(!nearby)mouseUntil=0;
   if(now<clickUntil){presencePending=false;return Source=MitaAttentionSource.Click;}
   if(now<mouseUntil){
    if(userUntil>0){userUntil=0;NextGlanceAt=now+Interval();}
    presencePending=false;return Source=MitaAttentionSource.Mouse;
   }
   if(fresh){
    if(now<userUntil)return Source=MitaAttentionSource.User;
    if((presencePending&&now-stableSince>=.4f)||now>=NextGlanceAt){
     presencePending=false;userUntil=now+.4f+(float)random.NextDouble()*.4f;
     NextGlanceAt=userUntil+Interval();return Source=MitaAttentionSource.User;
    }
   }
   return Source=MitaAttentionSource.Native;
  }
 }
}

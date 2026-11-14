using System;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintClock {
  bool initialized,restored;string previousMode;float displayed,cycle;
  public float CycleHour=>cycle;public float VisibleHour=>displayed;
  static float Wrap(double value)=>(float)((value%24+24)%24);
  public void RestoreCycle(float hour){if(float.IsNaN(hour)||float.IsInfinity(hour))throw new BlueprintImportException("invalid-setting: cycle phase");cycle=Wrap(hour);restored=true;}
  public float Sample(BlueprintSettings settings,DateTimeOffset localNow,float deltaSeconds,bool paused){
   settings.Validate();if(float.IsNaN(deltaSeconds)||float.IsInfinity(deltaSeconds)||deltaSeconds<0)throw new BlueprintImportException("invalid-setting: clock delta");
   bool enteringCycle=previousMode!="cycle"&&settings.TimeMode=="cycle";if(enteringCycle&&!restored)cycle=Wrap(settings.Hour);previousMode=settings.TimeMode;
   float target;
   if(settings.TimeMode=="fixed")target=Wrap(settings.Hour);
   else if(settings.TimeMode=="cycle"){if(!paused)cycle=Wrap(cycle+deltaSeconds*24/(settings.CycleMinutes*60));target=cycle;restored=false;}
   else target=Wrap(localNow.Hour+localNow.Minute/60.0+localNow.Second/3600.0+localNow.Millisecond/3600000.0+settings.OffsetHours);
   if(!initialized){displayed=target;initialized=true;return displayed;}if(paused)return displayed;
   if(settings.TimeMode=="system"){float difference=Wrap(target-displayed+12)-12;displayed=Wrap(displayed+difference*(1-(float)Math.Exp(-deltaSeconds/3)));}else displayed=target;
   return displayed;
  }
 }
}

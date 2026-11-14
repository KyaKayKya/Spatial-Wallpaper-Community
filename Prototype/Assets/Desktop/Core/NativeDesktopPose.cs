using System;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Desktop {
 // One view per original DesktopRuntime target, no camera/Tracker ownership.
 // Reuse the existing binocular filter, five-parameter map and hold-on-loss
 // smoother. Children receive values, never calibration or tracking authority.
 public sealed class NativeDesktopPose {
  readonly CalibrationProfile profile;readonly DesktopDisplay display;readonly bool keepHeight;
  readonly ViewportMetrics viewport;readonly BinocularSmoother binocular=new BinocularSmoother();
  readonly EyeSmoother smoother=new EyeSmoother(new[]{0.0,0,-.67},true);
  public NativeDesktopPose(CalibrationProfile applied,DesktopDisplay target,bool keepHeight){
   if(applied==null||applied.schema_version!=2||applied.manual==null||!applied.Valid()||!applied.manual.Bound||target==null||target.Id!=applied.manual.display_id)throw new ArgumentException("native-desktop-profile-target");
   profile=applied.Copy();display=new DesktopDisplay{Id=target.Id,Device=target.Device,X=target.X,Y=target.Y,Width=target.Width,Height=target.Height,RefreshNumerator=target.RefreshNumerator,RefreshDenominator=target.RefreshDenominator};this.keepHeight=keepHeight;
   if(!ViewportGeometry.TryMap(display.X,display.Y,display.Width,display.Height,display.X,display.Y,display.Width,display.Height,display.Width,display.Height,0,0,display.Width,display.Height,profile.manual.display_width,out viewport)||viewport.Width<.01||viewport.Height<.01||viewport.Width>10||viewport.Height>10)throw new ArgumentException("native-desktop-viewport");
  }
  public void UpdateRefresh(DesktopDisplay target){
   if(target==null||target.Id!=display.Id||!target.Matches(display.X,display.Y,display.Width,display.Height))throw new ArgumentException("native-desktop-refresh-target");
   display.RefreshNumerator=target.RefreshNumerator;display.RefreshDenominator=target.RefreshDenominator;
  }
  public JObject Advance(RawTrackingSample raw,ulong now,double deltaTime,bool paused,SoundMix sound=null){
   if(!TrackingCalibration.Finite(deltaTime)||deltaTime<0)throw new ArgumentException("native-desktop-delta-time");
   bool fresh=raw?.Binocular!=null&&raw.Fresh(now);TrackingSnapshot mapped=null;
   var observation=fresh?binocular.Update(raw.Stream,raw.Captured,raw.Binocular):null;
   if(fresh&&profile.manual.Matches(raw.Descriptor,display.Id)){
    var eye=profile.manual.Map(observation);if(TrackingCalibration.SafeEye(eye))mapped=new TrackingSnapshot(1,true,raw.Stream,raw.Frame,raw.Captured,eye:eye);
   }
   smoother.SetReference(new[]{0.0,0,-.67});smoother.Advance(mapped,now,deltaTime);var local=viewport.ToView(new[]{smoother.X,smoother.Y,smoother.Z});
   return new JObject{{"display_id",display.Id},{"width",viewport.Width},{"height",viewport.Height},{"keep_height",keepHeight},{"eye",new JArray(local)},{"fresh",mapped!=null},{"paused",paused},{"presentation",new JObject{{"numerator",display.RefreshNumerator},{"denominator",display.RefreshDenominator}}},
    // Original trusted Mita behaviour consumes the existing derived head/eye
    // metadata relay. It contains no camera pixels and is not an author API.
    {"tracking",DesktopTrackingState.Pack(raw,now)},{"sound",(sound??new SoundMix()).Json}};
  }
 }
}

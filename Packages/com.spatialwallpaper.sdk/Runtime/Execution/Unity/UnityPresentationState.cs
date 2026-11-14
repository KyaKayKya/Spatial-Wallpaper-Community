using System;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Sdk.Execution {
 // Static bounded data only. No author binding exposes Volume, cameras or RenderSettings.
 internal sealed class UnityPresentationState:IDisposable {
  readonly JObject config;readonly List<UnityEngine.Object> assets=new List<UnityEngine.Object>();
  readonly ResourceLease profileMemory;
  bool configured;AmbientMode previousMode;Color previousSky,previousEquator,previousGround;float previousIntensity,previousReflection;
  Camera camera;Color previousBackground;CameraClearFlags previousClearFlags;
  internal bool PostProcessing=>(bool)config["post"];
  static Color Color(JToken value)=>new Color((float)value[0],(float)value[1],(float)value[2],(float)value[3]);
  internal UnityPresentationState(JObject config,GameObject owner,ResourceLedger ledger=null){
   this.config=config;if(!PostProcessing)return;
   // A script may destroy the Volume's node while these static assets remain alive.
   // Keep their reservation separate from the node's render-retirement lease.
   profileMemory=ledger?.Reserve(new ResourceCost(16384,65536,0,5));
   try{
    var profile=ScriptableObject.CreateInstance<VolumeProfile>();assets.Add(profile);var volume=owner.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.sharedProfile=profile;
    var bloom=profile.Add<Bloom>(true);assets.Add(bloom);bloom.threshold.Override((float)config["threshold"]);bloom.intensity.Override((float)config["intensity"]);bloom.scatter.Override((float)config["scatter"]);
    var tone=profile.Add<Tonemapping>(true);assets.Add(tone);tone.mode.Override((bool)config["aces"]?TonemappingMode.ACES:TonemappingMode.None);
    var color=profile.Add<ColorAdjustments>(true);assets.Add(color);color.postExposure.Override((float)config["exposure"]);color.saturation.Override((float)config["saturation"]);
    var vignette=profile.Add<Vignette>(true);assets.Add(vignette);vignette.intensity.Override((float)config["vignette"]);vignette.smoothness.Override((float)config["smoothness"]);
    profileMemory?.Commit();
   }catch{Dispose();throw;}
  }
  internal void Configure(Camera camera){
   if(configured)throw new InvalidOperationException("presentation-already-configured");
   previousMode=RenderSettings.ambientMode;previousSky=RenderSettings.ambientSkyColor;previousEquator=RenderSettings.ambientEquatorColor;previousGround=RenderSettings.ambientGroundColor;previousIntensity=RenderSettings.ambientIntensity;configured=true;
   this.camera=camera;previousBackground=camera.backgroundColor;previousClearFlags=camera.clearFlags;
   // Environment reflection assets are outside this bounded presentation subset.
   // Do not inherit a baked sky reflection from the trusted Worker's build scene.
   previousReflection=RenderSettings.reflectionIntensity;RenderSettings.reflectionIntensity=0;
   if((bool)config["background"]){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color(config["backgroundColor"]);}
   if((bool)config["ambient"]){RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color(config["sky"]);RenderSettings.ambientIntensity=1;}
   else if((bool)config["gradient"]){RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=Color(config["sky"]);RenderSettings.ambientEquatorColor=Color(config["equator"]);RenderSettings.ambientGroundColor=Color(config["ground"]);RenderSettings.ambientIntensity=1;}
  }
  public void Dispose(){
   if(configured){RenderSettings.ambientMode=previousMode;RenderSettings.ambientSkyColor=previousSky;RenderSettings.ambientEquatorColor=previousEquator;RenderSettings.ambientGroundColor=previousGround;RenderSettings.ambientIntensity=previousIntensity;RenderSettings.reflectionIntensity=previousReflection;configured=false;}
   if(camera){camera.backgroundColor=previousBackground;camera.clearFlags=previousClearFlags;}camera=null;
   foreach(var asset in assets)if(asset){if(Application.isPlaying)UnityEngine.Object.Destroy(asset);else UnityEngine.Object.DestroyImmediate(asset);}assets.Clear();
   profileMemory?.Dispose();
  }
 }
}

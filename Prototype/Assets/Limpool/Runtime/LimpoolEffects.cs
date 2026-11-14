using System;
using UnityEngine;
using VolumetricLights;

public sealed class LimpoolEffects:MonoBehaviour {
 public PoolPlanarReflection reflection;
 public Material water;
 public GameObject waterLights;
 public VolumetricLight beam;
 Material instance,lightInstance;
 void Awake(){
  instance=new Material(water);water=instance;
  reflection.GetComponent<Renderer>().sharedMaterial=water;
  string[] args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"--limpool-quality");
  string quality=ix>=0&&ix+1<args.Length?args[ix+1]:"default";
  bool low=quality=="low";
  int debug=Array.IndexOf(args,"--water-debug");if(debug>=0)water.SetFloat("_DebugMode",float.Parse(args[debug+1],System.Globalization.CultureInfo.InvariantCulture));
  water.SetFloat("_WaterDepthClip",Array.IndexOf(args,"--water-depth-clip")>=0?1:0);
  if(Array.IndexOf(args,"--no-sun-spec")>=0)water.SetFloat("_SpecularStrength",0);
  if(Array.IndexOf(args,"--no-skylight-reflection")>=0)reflection.excludedLayers|=1<<29;
  if(Array.IndexOf(args,"--freeze-effects")>=0)water.SetFloat("_WaterTime",12);
  if(Array.IndexOf(args,"--raw-reflection")>=0)reflection.radianceSplitMaterial=null;
  Shader.SetGlobalFloat("_LimpoolReflectionFilter",0);Shader.SetGlobalFloat("_LimpoolAngularRoughness",0);
  if(Array.IndexOf(args,"--constant-reflection-filter")>=0)Shader.SetGlobalFloat("_LimpoolReflectionFilter",1);
  int roughness=Array.IndexOf(args,"--reflection-angular-roughness");if(roughness>=0)Shader.SetGlobalFloat("_LimpoolAngularRoughness",float.Parse(args[roughness+1],System.Globalization.CultureInfo.InvariantCulture));
  int normalStrength=Array.IndexOf(args,"--fresnel-normal-strength");if(normalStrength>=0)water.SetFloat("_FresnelNormalStrength",float.Parse(args[normalStrength+1],System.Globalization.CultureInfo.InvariantCulture));
  int weight=Array.IndexOf(args,"--fresnel-override");if(weight>=0)water.SetFloat("_FresnelOverride",float.Parse(args[weight+1],System.Globalization.CultureInfo.InvariantCulture));
  reflection.resolutionScale=low||quality=="reflection-low"?.25f:.5f;
  if(quality=="no-reflection"||Array.IndexOf(args,"--no-reflection")>=0){reflection.enabled=false;water.DisableKeyword("_REFLECTIONMODE_REALTIME");water.SetFloat("_ReflectionIntensity",0);}
  if(Array.IndexOf(args,"--no-water")>=0){reflection.enabled=false;reflection.GetComponent<Renderer>().enabled=false;}
  var receivers=waterLights.GetComponentsInChildren<Renderer>();lightInstance=new Material(receivers[0].sharedMaterial);foreach(var rr in receivers)rr.sharedMaterial=lightInstance;
  if(Array.IndexOf(args,"--no-caustics")>=0)lightInstance.SetFloat("_Underwater",0);
  if(Array.IndexOf(args,"--no-wall-light")>=0)lightInstance.SetFloat("_Wall",0);
  if(low||quality=="no-waterlight")waterLights.SetActive(false);
  if(low||quality=="no-volume"||Array.IndexOf(args,"--no-volume")>=0){beam.gameObject.SetActive(false);var sea=GameObject.Find("SeaLightColumns");if(sea)sea.SetActive(false);}
  if(Array.IndexOf(args,"--no-sea-light")>=0){var sea=GameObject.Find("SeaLightColumns");if(sea)sea.SetActive(false);}
  QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
 }
 void Start(){
  // Small-world sampling is applied by LimpoolVolumeSampling after plugin updates.
  var args=Environment.GetCommandLineArgs();
  if(Array.IndexOf(args,"--beam-no-shadows")>=0){beam.enableShadows=false;beam.Refresh();}
  int b=Array.IndexOf(args,"--beam-brightness");if(b>=0){beam.brightness=float.Parse(args[b+1],System.Globalization.CultureInfo.InvariantCulture);beam.UpdateMaterialProperties();}
 }
 void OnDestroy(){if(instance)Destroy(instance);if(lightInstance)Destroy(lightInstance);}
}

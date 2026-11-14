using UnityEditor;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VolumetricLights;

public static class LimpoolLook {
 const string Root="Assets/Limpool/";
 public static readonly Vector3 SunDirection=new Vector3(-.7f,-.5f,-.75f);
 public static void Apply(){
  UnityEngine.Object.FindObjectOfType<PoolPlanarReflection>().reflectBothSides=true;
  var display=GameObject.Find("DisplayCamera");
  var medium=display.GetComponent<LimpoolUnderwater>();if(!medium)medium=display.AddComponent<LimpoolUnderwater>();
  medium.water=GameObject.Find("WaterSurface").transform;medium.screenFrame=display.GetComponent<WindowProjection>().screenFrame;
  var underwater=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Underwater.mat");
  if(!underwater){underwater=new Material(Shader.Find("SpatialWallpaper/LimpoolUnderwater"));AssetDatabase.CreateAsset(underwater,Root+"Materials/Underwater.mat");}
  var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root+"Settings/Forward.asset");
  var mediumFeature=renderer.rendererFeatures.OfType<LimpoolUnderwaterFeature>().FirstOrDefault();
  if(!mediumFeature){mediumFeature=ScriptableObject.CreateInstance<LimpoolUnderwaterFeature>();mediumFeature.name="LimpoolUnderwater";renderer.rendererFeatures.Add(mediumFeature);AssetDatabase.AddObjectToAsset(mediumFeature,renderer);}
  mediumFeature.material=underwater;mediumFeature.Create();EditorUtility.SetDirty(mediumFeature);EditorUtility.SetDirty(renderer);
  var split=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/ReflectionSplit.mat");if(!split){split=new Material(Shader.Find("Hidden/Limpool/ReflectionSplit"));AssetDatabase.CreateAsset(split,Root+"Materials/ReflectionSplit.mat");}split.SetFloat("_RadianceLimit",.25f);EditorUtility.SetDirty(split);GameObject.Find("WaterSurface").GetComponent<PoolPlanarReflection>().radianceSplitMaterial=split;
  var water=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Water.mat");water.SetFloat("_FresnelNormalStrength",.264f);water.SetFloat("_FresnelOverride",-1);EditorUtility.SetDirty(water);
  var sun=GameObject.Find("LimpoolSun").GetComponent<Light>();sun.transform.rotation=Quaternion.LookRotation(SunDirection);sun.intensity=4.8f;sun.color=new Color(1,.91f,.79f);
  var skyTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Generated/kloppenheim_05_puresky_4k.hdr");
  var sky=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/Sky.mat");
  if(!sky){sky=new Material(Shader.Find("Skybox/Panoramic"));AssetDatabase.CreateAsset(sky,Root+"Materials/Sky.mat");}
  sky.SetTexture("_MainTex",skyTexture);sky.SetFloat("_Exposure",.65f);sky.SetFloat("_Rotation",115);sky.SetColor("_Tint",new Color(.38f,.52f,.61f));EditorUtility.SetDirty(sky);RenderSettings.skybox=sky;Camera.main.clearFlags=CameraClearFlags.Skybox;
  var plaster=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/NeutralPlaster.mat");
  var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"Generated/Wall_MetallicSmoothness.png");importer.sRGBTexture=false;importer.SaveAndReimport();
  plaster.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Generated/Wall_MetallicSmoothness.png"));plaster.EnableKeyword("_METALLICSPECGLOSSMAP");plaster.SetFloat("_Smoothness",1);plaster.SetFloat("_BumpScale",.85f);plaster.SetColor("_BaseColor",new Color(.78f,.79f,.76f));EditorUtility.SetDirty(plaster);
  var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"Settings/Appearance.asset");
  if(!profile.TryGet<Bloom>(out var bloom)){bloom=profile.Add<Bloom>(true);AssetDatabase.AddObjectToAsset(bloom,profile);}bloom.intensity.Override(.5f);bloom.threshold.Override(.85f);bloom.scatter.Override(.72f);bloom.highQualityFiltering.Override(true);EditorUtility.SetDirty(bloom);
  if(profile.TryGet<ColorAdjustments>(out var color)){color.postExposure.Override(.3f);color.contrast.Override(8);EditorUtility.SetDirty(color);}EditorUtility.SetDirty(profile);
  var beam=GameObject.Find("SkylightVolume").GetComponent<VolumetricLight>();beam.transform.rotation=sun.transform.rotation;beam.brightness=1.05f;beam.density=1.2f;beam.noiseStrength=.35f;beam.jittering=.0005f;beam.dithering=0;beam.penumbra=.85f;beam.useBlueNoise=true;beam.mediumAlbedo=new Color(.9f,.95f,1);beam.Refresh();
  var glow=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/WaterLight.mat");glow.SetFloat("_Strength",1.3f);EditorUtility.SetDirty(glow);
  var aperture=GameObject.Find("SkylightRadiance");
  if(!aperture){aperture=GameObject.CreatePrimitive(PrimitiveType.Cube);aperture.name="SkylightRadiance";Object.DestroyImmediate(aperture.GetComponent<Collider>());aperture.transform.SetParent(GameObject.Find("AuthoredMetres").transform,false);}
  aperture.transform.localPosition=new Vector3(2.83f,4.57f,7.2f);aperture.transform.localScale=new Vector3(1.84f,.01f,1.58f);
  aperture.layer=29;
  var luminous=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/SkylightRadiance.mat");if(!luminous){luminous=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(luminous,Root+"Materials/SkylightRadiance.mat");}luminous.SetColor("_BaseColor",new Color(2.2f,2.08f,1.9f));EditorUtility.SetDirty(luminous);
  var rr=aperture.GetComponent<Renderer>();rr.sharedMaterial=luminous;rr.shadowCastingMode=ShadowCastingMode.Off;rr.receiveShadows=false;
  var bottom=GameObject.Find("PoolBottomContinuation");if(bottom)bottom.transform.localPosition=new Vector3(0,-.11f,54);
  SeaLights(sun.transform.rotation);
  var sampling=beam.GetComponent<LimpoolVolumeSampling>();if(!sampling)sampling=beam.gameObject.AddComponent<LimpoolVolumeSampling>();sampling.step=.002f;sampling.maxSteps=256;
 }
 static void SeaLights(Quaternion direction){
  const float scale=.53f/8;
  var root=GameObject.Find("SeaLightColumns");if(!root){root=new GameObject("SeaLightColumns");root.transform.SetParent(GameObject.Find("LimpoolContent").transform,false);}
  var targets=new[]{new Vector3(-6,1.1f,18),new Vector3(2,1.1f,32),new Vector3(12,1.1f,55)};
  for(int i=0;i<targets.Length;i++){
   string name="SeaSunshaft"+i;var go=GameObject.Find(name);
   if(!go){go=new GameObject(name);go.transform.SetParent(root.transform,false);}
   Vector3 origin=targets[i]-SunDirection*(18/-SunDirection.y);
   go.transform.localPosition=(origin-new Vector3(0,2.25f,0))*scale;go.transform.rotation=direction;
   var lamp=go.GetComponent<Light>();if(!lamp)lamp=go.AddComponent<Light>();lamp.type=LightType.Spot;lamp.range=(origin-targets[i]).magnitude*scale+.2f;lamp.spotAngle=4+i*1.1f;lamp.intensity=0;lamp.enabled=false;
   var beam=go.GetComponent<VolumetricLight>();if(!beam)beam=go.AddComponent<VolumetricLight>();
   beam.alwaysOn=true;beam.brightnessMode=BrightnessMode.Absolute;beam.brightness=.8f;beam.density=.35f;beam.mediumAlbedo=new Color(.92f,.96f,1);beam.penumbra=.85f;
   beam.enableShadows=true;beam.shadowIntensity=1;beam.shadowNearDistance=.001f;beam.shadowCullingMask=1;beam.shadowResolution=VolumetricLights.ShadowResolution._512;beam.shadowBakeInterval=ShadowBakeInterval.OnStart;
   beam.useNoise=true;beam.noiseScale=2;beam.noiseStrength=.55f;beam.windDirection=new Vector3(.0007f,.0003f,.0005f);beam.dithering=0;beam.jittering=.0005f;beam.useBlueNoise=true;beam.animatedBlueNoise=false;
   beam.enableDustParticles=false;beam.renderQueue=3101;beam.targetCamera=Camera.main.transform;beam.Refresh();
   var motion=go.GetComponent<LimpoolSeaLight>();if(!motion)motion=go.AddComponent<LimpoolSeaLight>();motion.beam=beam;motion.baseBrightness=1.5f-i*.12f;motion.phase=i*2.1f;
   beam.density=.28f;beam.UpdateMaterialProperties();
   var sampling=go.GetComponent<LimpoolVolumeSampling>();if(!sampling)sampling=go.AddComponent<LimpoolVolumeSampling>();sampling.step=.008f;sampling.maxSteps=256;
   foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  }
 }
}

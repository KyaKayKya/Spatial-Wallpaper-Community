using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintLighting:MonoBehaviour {
  public Light Sun;public Volume Appearance;public Material Sky;public UniversalRenderPipelineAsset[] Pipelines;
  public string Weather="clear";Light[] windowLights=new Light[0];
  public void WindowGlow(bool enabled,float night){foreach(var light in windowLights)if(light){light.enabled=enabled&&night>.01f;light.color=new Color(1,.65f,.32f)*night;}}
  Material sky;VolumeProfile profile;int quality=-1;string look;float hour;BlueprintResources resources;Transform lanternGroup;int lanternQuality=-1;Light[] lanternLights;
  public void Initialize(BlueprintResources owner){resources=owner;if(Sky){sky=new Material(Sky);owner.Own(sky);RenderSettings.skybox=sky;}if(Appearance){Appearance.profile=null;profile=Appearance.profile;owner.Own(profile);foreach(var c in profile.components)owner.Own(c);}}
  public void Apply(string style,int qualityIndex,float timeOfDay){
   if(Pipelines!=null&&Pipelines.Length>0&&quality!=qualityIndex){quality=Mathf.Clamp(qualityIndex,0,Pipelines.Length-1);GraphicsSettings.defaultRenderPipeline=Pipelines[quality];QualitySettings.renderPipeline=Pipelines[quality];}
   look=style;hour=Mathf.Repeat(timeOfDay,24);
   float elevation=Mathf.Sin((hour-6)/12*Mathf.PI);
   float daylight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.08f,.22f,elevation));
   float twilight=(1-SmoothRange(0,.28f,Mathf.Abs(elevation)))*SmoothRange(-.15f,0,elevation);
   float overcast=Weather=="clear"?0:Weather=="cloudy"?.55f:.85f;
   Quaternion solar=Quaternion.Euler((hour-6)*15,-35,0),lunar=solar*Quaternion.Euler(180,0,0);
   if(Sun){Sun.transform.rotation=elevation>0?solar:lunar;Sun.intensity=elevation>0?1.8f*SmoothRange(0,.22f,elevation):.035f*SmoothRange(0,.22f,-elevation);Sun.intensity*=1-overcast*.7f;Sun.color=elevation>0?Color.Lerp(Color.white,new Color(1,.64f,.38f),twilight):new Color(.48f,.62f,1);}
   Color ambient=Color.Lerp(new Color(.012f,.018f,.035f),new Color(.65f,.72f,.82f),daylight)*(1-overcast*.25f);
   RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=ambient;RenderSettings.ambientEquatorColor=ambient*.85f;RenderSettings.ambientGroundColor=ambient*.55f;
   var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(ambient*.95f);probe.AddDirectionalLight(Vector3.up,ambient*.15f,1);RenderSettings.ambientProbe=probe;
   Color top=Color.Lerp(new Color(.003f,.008f,.025f),new Color(.18f,.45f,.75f),daylight),bottom=Color.Lerp(new Color(.015f,.025f,.06f),new Color(.68f,.82f,.95f),daylight);
   top=Color.Lerp(top,new Color(.10f,.12f,.26f),twilight*.75f);bottom=Color.Lerp(bottom,new Color(.65f,.28f,.19f),twilight*.85f);
   Color cloudTop=Color.Lerp(new Color(.006f,.01f,.025f),new Color(.32f,.37f,.43f),daylight),cloudBottom=Color.Lerp(new Color(.015f,.02f,.04f),new Color(.63f,.67f,.72f),daylight);
   top=Color.Lerp(top,cloudTop,overcast);bottom=Color.Lerp(bottom,cloudBottom,overcast);
   if(sky){RenderSettings.skybox=sky;sky.SetColor("_TopColor",top);sky.SetColor("_BottomColor",bottom);sky.SetFloat("_Exposure",1);sky.SetColor("_StarsTint",new Color(.7f,.8f,1,(1-daylight)*(1-overcast)));sky.SetColor("_SunTint",new Color(1,.78f,.5f,Mathf.Clamp01(elevation*12)*(1-overcast)));sky.SetColor("_MoonTint",new Color(.55f,.68f,1,(1-daylight)*(1-overcast)));sky.SetMatrix("sunMatrix",Matrix4x4.Rotate(Quaternion.Inverse(solar)));sky.SetMatrix("moonMatrix",Matrix4x4.Rotate(Quaternion.Inverse(lunar)));}
   RenderSettings.fog=Weather!="clear";RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=Color.Lerp(top,bottom,.6f);RenderSettings.fogDensity=Weather=="rain"?.22f:.17f;
   if(profile){if(profile.TryGet<Tonemapping>(out var tone))tone.mode.Override(style=="warm"?TonemappingMode.ACES:TonemappingMode.Neutral);if(profile.TryGet<Bloom>(out var bloom)){bloom.intensity.Override(qualityIndex==0?0:style=="clear"?.06f:style=="warm"?.38f:.2f);bloom.threshold.Override(.9f);bloom.scatter.Override(.62f);}if(profile.TryGet<ColorAdjustments>(out var color)){color.postExposure.Override(style=="clear"?.0f:style=="warm"?.15f:.05f);color.saturation.Override(style=="warm"?5:0);}}

  }
  static float SmoothRange(float from,float to,float value){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(from,to,value));}
  public void Lanterns(BlueprintDocument d,Transform blocks,float scale,int qualityIndex){
   if(lanternGroup&&lanternGroup.parent==blocks&&lanternQuality==qualityIndex){foreach(var l in lanternLights){l.range=7*scale;l.intensity=4*scale*scale;}foreach(var l in windowLights){l.range=5*scale;l.intensity=12*scale*scale;}return;}
   var previous=blocks.Find("Blueprint lantern lights");if(previous){previous.gameObject.SetActive(false);Destroy(previous.gameObject);}var group=new GameObject("Blueprint lantern lights").transform;group.SetParent(blocks,false);
   var positions=new System.Collections.Generic.List<Vector3>();var windows=new System.Collections.Generic.List<Vector3>();for(int y=0;y<d.Height;y++)for(int z=0;z<d.Length;z++)for(int x=0;x<d.Width;x++){var state=d.At(x,y,z);var position=blocks.TransformPoint(new Vector3(x+.5f,y+.55f,z+.5f));if(state.Name.EndsWith("lantern",StringComparison.Ordinal)||state.Name.EndsWith("torch",StringComparison.Ordinal))positions.Add(position);if(state.Name.Contains("glass"))windows.Add(position);}
   lanternGroup=group;lanternQuality=qualityIndex;int count=qualityIndex==0?2:qualityIndex==1?4:6,index=0;foreach(var p in positions.OrderBy(v=>v.z).Take(count)){
    var light=new GameObject("Warm lantern").AddComponent<Light>();light.transform.SetParent(group,false);light.transform.position=p;light.type=LightType.Point;light.color=new Color(1,.61f,.27f);light.range=7*scale;light.intensity=4*scale*scale;light.shadows=qualityIndex==2&&index++<2?LightShadows.Soft:LightShadows.None;light.shadowBias=.001f;light.shadowNormalBias=.002f;
   }lanternLights=group.GetComponentsInChildren<Light>();
   var chosen=new System.Collections.Generic.List<Vector3>();var fills=new System.Collections.Generic.List<Light>();foreach(var p in windows.OrderBy(v=>v.z)){if(chosen.Count>=(qualityIndex==0?2:4))break;if(chosen.Any(v=>Vector3.Distance(v,p)<4*scale))continue;chosen.Add(p);var fill=new GameObject("Warm window fill").AddComponent<Light>();fill.transform.SetParent(group,false);fill.transform.position=p+transform.TransformVector(Vector3.forward)*scale*.8f;fill.type=LightType.Point;fill.range=5*scale;fill.intensity=12*scale*scale;fill.color=new Color(1,.65f,.32f);fill.shadows=LightShadows.None;fills.Add(fill);}windowLights=fills.ToArray();
  }
 }
}

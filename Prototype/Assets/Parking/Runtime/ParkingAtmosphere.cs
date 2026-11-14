using System;
using UnityEngine;
namespace SpatialWallpaper.Parking {
 // Content owns its clock, materials and light outputs. Eye input is not a dependency.
 public sealed class ParkingAtmosphere:MonoBehaviour {
  public Renderer ground;
  public GameObject televisionStack;
  public Renderer[] screens;
  public Renderer mainFixture;
  public Light[] screenLights;
  public Light streetLight;
  public Light[] fieldLights=Array.Empty<Light>();
  public Light edgeLight;
  public Vector3[] screenCentres;
  public bool animate=true;
  public bool externalClock;
  public event Action FrameChanged;
  Material floor,originalFloor;Material[] crt,fixture,originalCrt,originalFixture;float nextTick;bool lastVisible;
  public bool TelevisionsVisible=>televisionStack&&televisionStack.activeInHierarchy;
  public float EvaluatedTime{get;private set;}
  void Awake(){
   originalFloor=ground.sharedMaterial;floor=ground.material;crt=new Material[screens.Length];originalCrt=new Material[screens.Length];
   for(int i=0;i<crt.Length;i++){originalCrt[i]=screens[i].sharedMaterial;crt[i]=screens[i].material;}
   originalFixture=mainFixture.sharedMaterials;fixture=mainFixture.materials;
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--parking-static")>=0)animate=false;
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--hide-tvs")>=0)televisionStack.SetActive(false);
   Evaluate(0);
  }
  public void SetTelevisionsVisible(bool visible){if(televisionStack)televisionStack.SetActive(visible);Evaluate(EvaluatedTime);}
  void LateUpdate(){if(floor&&lastVisible!=TelevisionsVisible)Evaluate(EvaluatedTime);}
  public static float ScreenSignal(float time,int index){
   float slow=Mathf.PerlinNoise(time*.7f,index*3.7f+11);
   float flutter=Mathf.PerlinNoise(time*8.7f,index*7.3f+31);
   float drop=Mathf.PerlinNoise(time*1.8f,index*13.1f+91)<.29f?.22f:1;
   return (.48f+slow*.85f+flutter*.38f)*drop;
  }
  public static float LampSignal(float time){
   float fault=Mathf.Repeat(time+1.7f,11.7f);
   return fault<.52f?.14f+.38f*Mathf.PerlinNoise(time*39,4):.89f+.11f*Mathf.PerlinNoise(time*3.1f,18);
  }
  void Update(){if(!externalClock&&animate&&Time.unscaledTime>=nextTick){nextTick=Time.unscaledTime+1f/24;Evaluate(Time.unscaledTime);}}
  public void Evaluate(float time){
   if(floor==null)return;
   EvaluatedTime=time;
   lastVisible=TelevisionsVisible;floor.SetFloat("_TVVisible",lastVisible?1:0);
   float screenScale=transform.lossyScale.x;
   float scaleRatio=screenScale/(.53f/12);
   for(int i=0;i<crt.Length;i++){
    float brightness=lastVisible?ScreenSignal(time,i):0;crt[i].SetFloat("_Brightness",brightness);crt[i].SetFloat("_NoiseTime",time);
    Vector3 p=screenCentres[i];floor.SetVector(i==0?"_TV0":"_TV1",new Vector4(p.x,p.y,p.z,brightness));
    if(i<screenLights.Length&&screenLights[i]){screenLights[i].intensity=.0002f*brightness*scaleRatio*scaleRatio;screenLights[i].range=2.4f*screenScale;}
   }
   float lamp=LampSignal(time);floor.SetFloat("_Lamp",lamp);
   if(streetLight){streetLight.intensity=.12f*lamp*scaleRatio*scaleRatio;streetLight.range=18*screenScale;streetLight.shadowNearPlane=.005f*scaleRatio;}
   foreach(var light in fieldLights)if(light){light.intensity=.12f*scaleRatio*scaleRatio;light.range=20*screenScale;light.shadowNearPlane=.005f*scaleRatio;}
   if(edgeLight){edgeLight.intensity=.075f*scaleRatio*scaleRatio;edgeLight.range=16*screenScale;}
   foreach(var m in fixture)if(m.HasProperty("_LampEmission"))m.SetColor("_LampEmission",new Color(.65f,.8f,1)*3*lamp);
   FrameChanged?.Invoke();
  }
  void OnDestroy(){
   if(ground&&ground.sharedMaterial==floor)ground.sharedMaterial=originalFloor;
   if(crt!=null)for(int i=0;i<crt.Length;i++){if(screens[i]&&screens[i].sharedMaterial==crt[i])screens[i].sharedMaterial=originalCrt[i];if(crt[i])Destroy(crt[i]);}
   if(mainFixture&&fixture!=null){var current=mainFixture.sharedMaterials;for(int i=0;i<Mathf.Min(current.Length,fixture.Length);i++)if(current[i]==fixture[i])current[i]=originalFixture[i];mainFixture.sharedMaterials=current;}
   if(floor)Destroy(floor);if(fixture!=null)foreach(var m in fixture)if(m)Destroy(m);
  }
 }
}

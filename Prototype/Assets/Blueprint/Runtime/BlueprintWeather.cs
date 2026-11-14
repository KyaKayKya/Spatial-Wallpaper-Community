using System.Collections.Generic;
using UnityEngine;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintWeather:MonoBehaviour {
  readonly List<ParticleSystem> systems=new List<ParticleSystem>();Transform root;string signature;bool paused;
  public void Invalidate(){signature=null;}
  public void Apply(BlueprintScene scene,BlueprintSettings settings){
   string next=settings.Weather+"|"+settings.WeatherIntensity+"|"+settings.Smoke+"|"+settings.Fireflies+"|"+settings.Quality;
   if(signature==next)return;signature=next;Clear();root=new GameObject("Blueprint atmosphere").transform;root.SetParent(transform,false);
   if(settings.Weather=="rain"||settings.Weather=="snow"){
    bool snow=settings.Weather=="snow";var p=Make("Weather",scene,new Vector3(0,.75f,1),snow?new Color(.9f,.94f,1,.75f):new Color(.6f,.75f,.9f,.45f),snow?.007f:.002f,snow?7:2,settings.Quality==0?180:500);
    var main=p.main;main.startSpeed=0;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(3,.01f,3);var emission=p.emission;emission.rateOverTime=(snow?35:100)*settings.WeatherIntensity;var velocity=p.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.y=snow?-.12f:-.9f;velocity.x=.04f;
    var renderer=p.GetComponent<ParticleSystemRenderer>();if(!snow){renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=10;renderer.velocityScale=.025f;}
    var collision=p.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;collision.quality=ParticleSystemCollisionQuality.Medium;collision.collidesWith=~(1<<4);collision.enableDynamicColliders=false;collision.lifetimeLoss=1;collision.maxCollisionShapes=16;
   }
   if(settings.Smoke){int count=0;var d=scene.Document;for(int y=0;y<d.Height&&count<4;y++)for(int z=0;z<d.Length&&count<4;z++)for(int x=0;x<d.Width&&count<4;x++)if(d.At(x,y,z).Name.EndsWith("campfire")&&d.At(x,y,z).Property("lit","true")=="true"){
    var p=Make("Campfire smoke",scene,Vector3.zero,new Color(.6f,.6f,.62f,.17f),.35f,5,40);p.transform.SetParent(scene.BlockOrigin,false);p.transform.localPosition=new Vector3(x+.5f,y+1,z+.5f);var main=p.main;main.startSpeed=.6f;main.startSize=.35f;var emission=p.emission;emission.rateOverTime=3;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=8;shape.radius=.1f;count++;
   }}
   if(settings.Fireflies){var p=Make("Evening fireflies",scene,new Vector3(0,-.05f,.65f),new Color(2,1.5f,.3f,.8f),.003f,8,24);var main=p.main;main.startSpeed=.008f;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.5f,.25f,1.2f);var emission=p.emission;emission.rateOverTime=2;var noise=p.noise;noise.enabled=true;noise.strength=.025f;noise.frequency=.3f;}
   SetPaused(Time.timeScale==0);
  }
  ParticleSystem Make(string name,BlueprintScene scene,Vector3 position,Color color,float size,float lifetime,int maximum){var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.localPosition=position;var p=go.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=p.main;main.loop=true;main.startLifetime=lifetime;main.startSize=size;main.startColor=color;main.maxParticles=maximum;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.playOnAwake=false;var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=Resources.Load<Material>("Blueprint/Materials/Particles");renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;systems.Add(p);p.Play();return p;}
  public void Hour(float hour){foreach(var p in systems)if(p&&p.name=="Evening fireflies"){var emission=p.emission;emission.enabled=hour>=18||hour<6;}}
  public void SetPaused(bool value){if(paused==value)return;paused=value;foreach(var p in systems)if(p){if(value)p.Pause();else p.Play();}}
  void Clear(){foreach(var p in systems)if(p)Destroy(p.gameObject);systems.Clear();if(root){root.gameObject.SetActive(false);Destroy(root.gameObject);}paused=false;}
  void OnDestroy(){Clear();}
 }
}

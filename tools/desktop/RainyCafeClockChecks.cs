using System;
using SpatialWallpaper.RainyCafe;
// Minimal particle API double: execute the production weather scheduler without rendering.
namespace UnityEngine {
 public class MonoBehaviour {public Transform transform=new Transform();public static implicit operator bool(MonoBehaviour x)=>x!=null;}
 public class Transform {public Vector3 lossyScale=new Vector3(1,1,1);}
 public struct Vector3 {public float x;public Vector3(float x,float y,float z){this.x=x;}}
 public class Light:MonoBehaviour {public bool enabled;public float range,intensity;}
 public class ParticleSystemRenderer {public bool enabled;}
 public class ParticleSystem {public int particleCount,steps,resets;public T[] GetComponentsInChildren<T>()=>new T[0];public void Pause(bool children){}public void Simulate(float t,bool children,bool restart,bool fixedStep){if(restart)resets++;else steps++;}}
 public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);public static int FloorToInt(float x)=>(int)Math.Floor(x);}
}
namespace SpatialWallpaper.RainyCafe {public static class RainyCafeAtmosphere {public const float CandleIntensityEncoding=64;}}
class RainyCafeClockChecks {
 static void Require(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  var layer=new UnityEngine.ParticleSystem();var weather=new RainyCafeParticleWeather {layers=new[]{layer}};
  weather.Evaluate(3600,true);Require(layer.steps<=240,"late-cafe-entry-has-bounded-particle-work");
  int steps=layer.steps;weather.Evaluate(3600,true);Require(layer.steps==steps,"same-clock-does-not-resimulate");
  weather.Evaluate(3600+1f/60,true);Require(layer.steps>steps&&layer.steps-steps<=2,"next-clock-step-advances-rain");
  weather.Evaluate(0,true);Require(layer.resets==2,"rewinding-clock-resets-particles");
  weather.Evaluate(0,false);steps=layer.steps;weather.Evaluate(7200,true);Require(layer.steps-steps<=240,"reenabling-rain-after-gap-has-bounded-work");
 }
}

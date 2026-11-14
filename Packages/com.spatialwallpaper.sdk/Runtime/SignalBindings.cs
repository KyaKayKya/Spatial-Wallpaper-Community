using System;
using UnityEngine;
namespace SpatialWallpaper.Sdk {
 public enum SignalKind { Flicker, Intermittent }
 public enum MaterialSignalKind { Float, VectorW, Color, Time }
 [Serializable]public sealed class MaterialSignalBinding {public Material material;public string property;public MaterialSignalKind kind;public Vector4 basis=Vector4.one;public float gain=1;}
 [Serializable]public sealed class LightSignalBinding {public Light light;public float intensity;}
 // Two bounded platform signals extracted from current material/light consumers.
 // No expression language, reflection, arbitrary callback or author code.
 public sealed class SignalBindings:MonoBehaviour {
  public SignalKind kind;public int seed;public MaterialSignalBinding[] materials=Array.Empty<MaterialSignalBinding>();public LightSignalBinding[] lights=Array.Empty<LightSignalBinding>();
  [NonSerialized]public Transform scaleRoot;[NonSerialized]public float authorScale=1;float next;float started;
  public static float Evaluate(SignalKind kind,float time,int seed){if(kind==SignalKind.Intermittent){float fault=Mathf.Repeat(time+1.7f,11.7f);return fault<.52f?.14f+.38f*Mathf.PerlinNoise(time*39,4):.89f+.11f*Mathf.PerlinNoise(time*3.1f,18);}float slow=Mathf.PerlinNoise(time*.7f,seed*3.7f+11),flutter=Mathf.PerlinNoise(time*8.7f,seed*7.3f+31),drop=Mathf.PerlinNoise(time*1.8f,seed*13.1f+91)<.29f?.22f:1;return (.48f+slow*.85f+flutter*.38f)*drop;}
  void OnEnable(){started=Time.unscaledTime;Apply(0);}
  void Update(){if(Time.unscaledTime>=next){next=Time.unscaledTime+1f/24;Apply(Time.unscaledTime-started);}}
  public void Apply(float time){float value=Evaluate(kind,time,seed),scale=scaleRoot?scaleRoot.lossyScale.x/authorScale:1;foreach(var binding in materials){if(!binding.material)continue;switch(binding.kind){case MaterialSignalKind.Float:binding.material.SetFloat(binding.property,value*binding.gain);break;case MaterialSignalKind.VectorW:var v=binding.basis;v.w=value*binding.gain;binding.material.SetVector(binding.property,v);break;case MaterialSignalKind.Color:binding.material.SetColor(binding.property,(Color)(binding.basis*(value*binding.gain)));break;case MaterialSignalKind.Time:binding.material.SetFloat(binding.property,time*binding.gain);break;}}foreach(var binding in lights)if(binding.light)binding.light.intensity=binding.intensity*value*(binding.light.type==LightType.Directional?1:scale*scale);}
 }
}

using System;
using UnityEngine;
namespace SpatialWallpaper.Sdk {
 [Serializable]public sealed class TransformHarmonic {public Vector3 amplitude,frequency,phase;public Vector3 Evaluate(float t)=>new Vector3(amplitude.x*Mathf.Sin(frequency.x*t+phase.x),amplitude.y*Mathf.Sin(frequency.y*t+phase.y),amplitude.z*Mathf.Sin(frequency.z*t+phase.z));}
 public sealed class TransformOscillation:MonoBehaviour {
  public TransformHarmonic[] position=Array.Empty<TransformHarmonic>(),rotation=Array.Empty<TransformHarmonic>();Vector3 rest;Quaternion orientation;float started;
  void OnEnable(){rest=transform.localPosition;orientation=transform.localRotation;started=Time.timeSinceLevelLoad;Apply(0);}
  public void Apply(float time){var p=Vector3.zero;var r=Vector3.zero;foreach(var h in position)p+=h.Evaluate(time);foreach(var h in rotation)r+=h.Evaluate(time);transform.localPosition=rest+p;transform.localRotation=Quaternion.Euler(r)*orientation;}
  void Update(){Apply(Time.timeSinceLevelLoad-started);}
  void OnDisable(){transform.localPosition=rest;transform.localRotation=orientation;}
 }
}

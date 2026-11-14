using UnityEngine;
using VolumetricLights;

// Slow cloud-transmission variation. Direction and geometry remain stationary.
public sealed class LimpoolSeaLight:MonoBehaviour {
 public VolumetricLight beam;
 public float baseBrightness=.8f;
 public float phase;
 void Update(){
  float t=Time.time;
  beam.brightness=baseBrightness*(1+.10f*Mathf.Sin(t*.071f+phase)+.06f*Mathf.Sin(t*.113f+phase*2.3f));
 }
}

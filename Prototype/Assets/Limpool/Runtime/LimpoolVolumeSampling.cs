using UnityEngine;
using VolumetricLights;

// VL 20.2.1 validates a 0.1-world-unit minimum step when updating materials.
// Our display scene is scaled to metres of a physical monitor. Override only
// these scene-owned material parameters after the plugin's LateUpdate.
[DefaultExecutionOrder(1000)]
public sealed class LimpoolVolumeSampling:MonoBehaviour {
 public float step=.002f;
 public int maxSteps=256;
 VolumetricLight beam;
 Renderer volumeRenderer;
 static readonly int Settings=Shader.PropertyToID("_RayMarchSettings");
 static readonly int Steps=Shader.PropertyToID("_RayMarchMaxSteps");
 void Awake(){beam=GetComponent<VolumetricLight>();volumeRenderer=GetComponent<Renderer>();}
 void LateUpdate(){
  if(!volumeRenderer||!volumeRenderer.sharedMaterial)return;
  var material=volumeRenderer.sharedMaterial;
  material.SetVector(Settings,new Vector4(beam.raymarchQuality,beam.dithering*.001f,beam.jittering,step));
  material.SetInt(Steps,maxSteps);
 }
}

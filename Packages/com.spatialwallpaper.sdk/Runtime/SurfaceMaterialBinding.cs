using System;
using UnityEngine;
namespace SpatialWallpaper.Sdk {
 public sealed class SurfaceMaterialBinding:MonoBehaviour {
  public Material[] materials=Array.Empty<Material>();public float depthPerScale=1;public Vector4 causticsTiling=Vector4.one;
  public void Apply(){float scale=transform.lossyScale.x;if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<=.00001f)throw new InvalidOperationException("surface-scale-invalid");foreach(var material in materials){if(material.HasProperty("_WaterLevel")){material.SetFloat("_WaterLevel",transform.position.y);material.SetFloat("_WorldScale",scale);material.SetFloat("_Animation",1);}if(material.HasProperty("_DepthEnd")){material.SetFloat("_DepthEnd",depthPerScale*scale);material.SetFloat("_CausticsEnd",depthPerScale*scale);material.SetVector("_CausticsTiling",causticsTiling/scale);}}}
  void LateUpdate(){Apply();}
 }
}

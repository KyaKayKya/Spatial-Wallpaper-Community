using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
namespace SpatialWallpaper.Parking {
 // Explicit ambient irradiance avoids depending on an Editor-generated sky probe.
 public sealed class ParkingEnvironmentBinding:MonoBehaviour {
  public Color irradiance=new Color(.07f,.075f,.085f);
  static readonly List<ParkingEnvironmentBinding> owners=new List<ParkingEnvironmentBinding>();
  static SphericalHarmonicsL2 original,applied;
  static AmbientMode originalMode;
  void OnEnable(){if(owners.Count==0){original=RenderSettings.ambientProbe;originalMode=RenderSettings.ambientMode;}owners.Add(this);Apply();}
  static void Apply(){var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(owners[owners.Count-1].irradiance);RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientProbe=probe;applied=probe;}
  void OnDisable(){
   bool top=owners.Count>0&&owners[owners.Count-1]==this;owners.Remove(this);
   if(!top||RenderSettings.ambientMode!=AmbientMode.Custom||!RenderSettings.ambientProbe.Equals(applied))return;
   if(owners.Count>0)Apply();else{RenderSettings.ambientMode=originalMode;RenderSettings.ambientProbe=original;}
  }
 }
}

using UnityEngine;
namespace SpatialWallpaper.Parking {
 // Host-side adapter. Removing/replacing the content also removes this subscription.
 public sealed class ParkingRenderBinding:MonoBehaviour {
  public ParkingAtmosphere content;
  public AdaptiveRenderCadence cadence;
  void OnEnable(){if(content)content.FrameChanged+=Wake;}
  void OnDisable(){if(content)content.FrameChanged-=Wake;}
  void Wake(){if(cadence)cadence.Wake();}
 }
}

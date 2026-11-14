using UnityEngine;
namespace SpatialWallpaper.Parking {
 public sealed class ParkingControls:MonoBehaviour {
  public ParkingAtmosphere atmosphere;
  void Update(){if(Input.GetKeyDown(KeyCode.T)&&atmosphere)atmosphere.SetTelevisionsVisible(!atmosphere.TelevisionsVisible);}
 }
}

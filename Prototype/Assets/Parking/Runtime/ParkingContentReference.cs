using UnityEngine;
namespace SpatialWallpaper.Parking {
 // Host-side reference for replacing content; neither asset depends on the eye input source.
 public sealed class ParkingContentReference:MonoBehaviour {
  public GameObject prefab;
  public Texture2D expectedGroundLightmap;
 }
}

using UnityEngine;
namespace SpatialWallpaper.Train {
 // The display adapter depends on content; content never depends on a camera or tracker.
 public sealed class TrainRenderBinding:MonoBehaviour {
  public TrainJourney content;
  public AdaptiveRenderCadence cadence;
  void Update(){if(content&&content.isActiveAndEnabled&&content.animate&&cadence)cadence.Wake();}
 }
}

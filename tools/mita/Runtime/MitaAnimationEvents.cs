using UnityEngine;

namespace SpatialWallpaper.Mita {
 // Only the verified native wink event is retained from the recovered clips.
 public sealed class MitaAnimationEvents:MonoBehaviour {
  public MitaEffects effects;
  public void PlayWink(){effects.Wink();}
 }
}

using UnityEngine;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk.Hosting {
 [DefaultExecutionOrder(-32000)]
 internal sealed class CapabilityFramePump:MonoBehaviour {
  CapabilityCalls calls;
  internal void Initialize(CapabilityCalls value){calls=value;calls.AdvanceFrame();}
  void Update(){calls?.AdvanceFrame();}
 }
}

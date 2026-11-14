using UnityEngine;
public sealed class ParkingBoxSettings : ScriptableObject {
 [Tooltip("Depth in authored parking metres. Rebuild the Parking scene after editing. Width 12 and height 6.75 preserve the showcase aperture.")]
 [Range(39, 360)] public float depth = 360;
}

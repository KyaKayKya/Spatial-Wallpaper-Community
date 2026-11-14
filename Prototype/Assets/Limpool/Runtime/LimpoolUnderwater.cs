using UnityEngine;

// The observer is outside the virtual pool. Water attenuation starts at the
// display plane, never in the physical space between the display and the eye.
public sealed class LimpoolUnderwater:MonoBehaviour {
 public Transform water;
 public Transform screenFrame;
}

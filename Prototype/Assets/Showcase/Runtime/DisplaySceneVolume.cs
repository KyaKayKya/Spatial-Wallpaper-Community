using UnityEngine;
// Authoring contract only: no camera, tracker, renderer, collider or global shader state.
public sealed class DisplaySceneVolume : MonoBehaviour {
 public Vector3 size = new Vector3(.53f, .298125f, 2);
 public bool showGuide;
 public Bounds LocalBounds => new Bounds(new Vector3(0, 0, size.z * .5f), size);
 public Vector3 FrontCorner(float x, float y) => transform.TransformPoint(new Vector3(x * size.x, y * size.y, 0));
 public bool ContainsWorld(Vector3 point, float tolerance = .00001f) {
  var b = LocalBounds; b.Expand(tolerance * 2); return b.Contains(transform.InverseTransformPoint(point));
 }
 void OnDrawGizmosSelected() {
  if (!showGuide) return;
  var previous = Gizmos.matrix; Gizmos.matrix = transform.localToWorldMatrix;
  Gizmos.color = Color.cyan; Gizmos.DrawWireCube(LocalBounds.center, size); Gizmos.matrix = previous;
 }
}

#if UNITY_EDITOR
using UnityEngine;
// Editor-only author fixtures. No component is serialized into a product scene.
[Cilboxable]
public sealed class NativeBridgeCompilerProbe:MonoBehaviour {
 public Transform target;
 void Update(){target.localPosition=new Vector3(1,2,3);}
}
[Cilboxable]
public sealed class NativeBridgeUnsupportedProbe:MonoBehaviour {
 public Mesh shape;
 void Update(){shape.Clear();}
}
#endif

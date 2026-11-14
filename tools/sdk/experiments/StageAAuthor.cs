// Original example, MIT. Compiled only in the author/editor assembly.
using UnityEngine;
[Cilboxable]
public sealed class StageAAuthor:MonoBehaviour {
 public Transform target;
 public int count;
 void Start(){count=1;}
 void Update(){count=count+1;target.Rotate(0,1,0);}
}

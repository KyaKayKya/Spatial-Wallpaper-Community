using UnityEngine;
// A variant is valid only for its authored mesh and placement relative to this cabinet.
public sealed class ReplaceableLightingVariant:MonoBehaviour {
 public CabinetLightingReceivers cabinet;
 public Material[] occupied;
 [SerializeField] MeshRenderer sculpture;
 [SerializeField] Mesh bakedMesh;
 [SerializeField] Matrix4x4 bakedPlacement;
 [SerializeField] bool hasBakedPlacement;
 bool applied, warned;
 public void RecordBakedPlacement(MeshRenderer renderer,CabinetLightingReceivers receiver){
  sculpture=renderer;cabinet=receiver;
  bakedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
  bakedPlacement=receiver.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
  hasBakedPlacement=true;
 }
 bool Matches(){
  if(!hasBakedPlacement||!cabinet||!sculpture||!sculpture.enabled||!sculpture.gameObject.activeInHierarchy)return false;
  var filter=sculpture.GetComponent<MeshFilter>();
  if(!filter||filter.sharedMesh!=bakedMesh)return false;
  var now=cabinet.transform.worldToLocalMatrix*sculpture.transform.localToWorldMatrix;
  for(int i=0;i<16;i++)if(Mathf.Abs(now[i]-bakedPlacement[i])>.00001f*Mathf.Max(1,Mathf.Abs(bakedPlacement[i])))return false;
  return true;
 }
 void OnEnable(){
  applied=false;warned=false;
  if(!cabinet)cabinet=GetComponentInParent<PhysicalDisplayRig>()?.GetComponentInChildren<CabinetLightingReceivers>();
  Evaluate();
 }
 void OnDisable(){if(cabinet)cabinet.Release(this);applied=false;}
 void LateUpdate(){Evaluate();}
 void Evaluate(){
  bool valid=Matches();
  if(valid&&!applied){cabinet.UseVariant(this,occupied);applied=true;warned=false;}
  if(!valid){
   if(cabinet)cabinet.Release(this);
   applied=false;
   if(!warned){Debug.LogWarning("Baked lighting variant is absent or differs from its authored mesh/placement. Cabinet uses empty lighting.",this);warned=true;}
  }
 }
}

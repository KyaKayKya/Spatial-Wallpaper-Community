using UnityEngine;
[DefaultExecutionOrder(-400)]
public sealed class CabinetLightingReceivers:MonoBehaviour {
 public MeshRenderer[] surfaces;
 public Material[] emptyMaterials;
 public AdaptiveRenderCadence cadence;
 public Object Owner{get;private set;}
 void Awake(){Owner=null;Apply(emptyMaterials);}
 public void UseVariant(Object owner,Material[] materials){
  if(!owner||materials==null||surfaces==null||materials.Length!=surfaces.Length)return;
  Owner=owner;Apply(materials);
 }
 public void Release(Object owner){if(Owner==owner){Owner=null;Apply(emptyMaterials);}}
 void Apply(Material[] materials){
  if(materials==null||surfaces==null||materials.Length!=surfaces.Length)return;
  for(int i=0;i<surfaces.Length;i++)if(surfaces[i]&&materials[i])surfaces[i].sharedMaterial=materials[i];
  if(cadence)cadence.Wake();
 }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// Own per-scene materials so quality, water height and reflection textures never alter assets.
[DefaultExecutionOrder(-400)]
public sealed class PoolMaterialState : MonoBehaviour {
 public Transform waterSurface;
 readonly Dictionary<Material,Material> owned=new Dictionary<Material,Material>();
 bool frozen;
 Material Own(Material source){
  if(!source)return null;
  if(!owned.TryGetValue(source,out var instance)){instance=new Material(source);owned.Add(source,instance);}
  return instance;
 }
 void Awake(){
  var args=Environment.GetCommandLineArgs();int qualityIndex=Array.IndexOf(args,"--pool-quality");
  frozen=qualityIndex>=0&&qualityIndex+1<args.Length&&args[qualityIndex+1]=="static";
  foreach(var renderer in GetComponentsInChildren<Renderer>(true)){
   var materials=renderer.sharedMaterials;
   for(int i=0;i<materials.Length;i++)materials[i]=Own(materials[i]);
   renderer.sharedMaterials=materials;
  }
  var animation=GetComponent<PoolAnimation>();animation.water=Own(animation.water);animation.tiles=Own(animation.tiles);
  var planar=GetComponentInChildren<PoolPlanarReflection>();planar.reflectionMaterial=Own(planar.reflectionMaterial);
 }
 void LateUpdate(){
  foreach(var m in owned.Values)if(m.HasProperty("_WaterLevel")){
   m.SetFloat("_WaterLevel",waterSurface.position.y);
   m.SetFloat("_WorldScale",waterSurface.lossyScale.x);
   m.SetFloat("_Animation",frozen?0:1);
  }
  var water=GetComponent<PoolAnimation>().water;
  if(water.HasProperty("_DepthEnd")){
   float ratio=waterSurface.lossyScale.x/(.53f/11.2f);
   water.SetFloat("_DepthEnd",.12f*ratio);
   water.SetFloat("_CausticsEnd",.12f*ratio);
   water.SetVector("_CausticsTiling",new Vector4(.2f,.2f,.3f,.3f)/waterSurface.lossyScale.x);
  }
 }
 void OnDestroy(){foreach(var m in owned.Values)if(m)Destroy(m);owned.Clear();}
}

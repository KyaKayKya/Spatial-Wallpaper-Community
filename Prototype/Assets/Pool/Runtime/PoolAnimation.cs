using UnityEngine;
using System;

public sealed class PoolAnimation:MonoBehaviour {
 public Transform ring;
 public Material water,tiles;
 public ContinuousRenderDemand demand;
 Vector3 origin;Quaternion rotation;
 public string Quality{get;private set;}="high";
 void Start(){
  // The source ring has an authored tilt. Level its thin mesh axis before adding
  // a small visual bob; keep source orientation around the vertical axis.
  var mesh=ring.GetComponent<MeshFilter>().sharedMesh;
  var size=mesh.bounds.size;
  Vector3 axis=size.x<size.y&&size.x<size.z?Vector3.right:size.y<size.z?Vector3.up:Vector3.forward;
  var normal=ring.TransformDirection(axis);
  if(Vector3.Dot(normal,Vector3.up)<0)normal=-normal;
  ring.rotation=Quaternion.FromToRotation(normal,Vector3.up)*ring.rotation;
  var surface=GetComponent<PoolMaterialState>().waterSurface;
  float scale=surface.lossyScale.x;
  float bottom=ring.GetComponent<Renderer>().bounds.min.y;
  ring.position+=Vector3.up*(surface.position.y-.10f*scale-bottom);
  origin=ring.localPosition;rotation=ring.localRotation;
  var args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"--pool-quality");
  if(ix>=0&&ix+1<args.Length)Quality=args[ix+1];
  if(Quality=="low")demand.SetInterval(2);
  if(Quality=="static"){
   demand.enabled=false;
   if(water.HasProperty("_Animation"))water.SetFloat("_Animation",0);
   if(water.HasProperty("_NormalMapASpeeds"))water.SetVector("_NormalMapASpeeds",Vector4.zero);
   if(water.HasProperty("_CausticsSpeed"))water.SetVector("_CausticsSpeed",Vector4.zero);
   if(water.HasProperty("_CausticsSpeed3D"))water.SetVector("_CausticsSpeed3D",Vector4.zero);
   if(water.HasProperty("_ReflectionIntensity"))water.SetFloat("_ReflectionIntensity",0);
   tiles.SetFloat("_Animation",0);
  }
 }
 void Update(){
  if(!ring||Quality=="static")return;
  float t=Time.timeSinceLevelLoad;
  ring.localPosition=origin+new Vector3(.04f*Mathf.Sin(t*.31f),.045f*Mathf.Sin(t*1.1f)+.012f*Mathf.Sin(t*1.73f),.03f*Mathf.Sin(t*.23f));
  ring.localRotation=Quaternion.Euler(.55f*Mathf.Sin(t*.8f),1.5f*Mathf.Sin(t*.19f),.45f*Mathf.Cos(t*.7f))*rotation;
 }
}

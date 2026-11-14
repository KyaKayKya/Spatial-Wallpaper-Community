using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SpatialWallpaper.Train;

public static class TrainGeometryValidation {
 static bool RenderTriangleHit(MeshFilter filter,Vector3 origin,Vector3 direction){
  var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
  float best=float.PositiveInfinity;string diagnostic="";
  for(int i=0;i<vertices.Length;i++)vertices[i]=filter.transform.TransformPoint(vertices[i]);
  for(int i=0;i<indices.Length;i+=3){
   var a=vertices[indices[i]];var e1=vertices[indices[i+1]]-a;var e2=vertices[indices[i+2]]-a;
   var h=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<=1e-12f)continue;
   var s=origin-a;float u=Vector3.Dot(s,h)/det;
   var q=Vector3.Cross(s,e1);float v=Vector3.Dot(direction,q)/det;
   float t=Vector3.Dot(e2,q)/det;float score=Mathf.Max(0,-u,-v,u+v-1);
   if(t>=0&&score<best){best=score;diagnostic=$"mesh={filter.name} triangle={i/3} u={u:R} v={v:R} det={det:R} t={t:R} a="+a.ToString("F8")+" b="+(a+e1).ToString("F8")+" c="+(a+e2).ToString("F8");}
   // FBX split vertices differ by ~1e-8 metres at the central UV seam.
   // Barycentric tolerance is far below a display pixel on these triangles.
   if(det<0||u<-.00001f||u>1.00001f||v<-.00001f||u+v>1.00001f)continue;
   if(Vector3.Dot(e2,q)/det>=0)return true;
  }
  Debug.LogWarning("Triangle miss diagnostic: "+diagnostic+" violation="+best);return false;
 }
 public static void Validate(TrainJourney content,DisplaySceneVolume box,float scale){
  // Check the whole analytic suspension envelope, not just captured phases.
  foreach(var f in content.furnishings.GetComponentsInChildren<MeshFilter>()){
   var b=f.sharedMesh.bounds;
   foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f}){
    var p=f.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z)));
    foreach(float dx in new[]{-.005f,.005f})foreach(float dy in new[]{-.005f,.005f})
     if(!box.ContainsWorld(p+new Vector3(dx,dy,0)*scale,.00002f))throw new Exception("Train suspension envelope outside volume: "+f.name);
   }
  }
  // Temporary colliders validate the actual imported triangles; none are saved.
  var colliders=new List<MeshCollider>();var layers=new Dictionary<GameObject,int>();
  bool oldBackfaces=Physics.queriesHitBackfaces;int rays=0,windows=0,triangleFallbacks=0;
  try{
   Physics.queriesHitBackfaces=false;
   foreach(var f in content.GetComponentsInChildren<MeshFilter>()){
    if(f.name.StartsWith("WindowLayer_"))continue;
    layers.Add(f.gameObject,f.gameObject.layer);f.gameObject.layer=31;
    var collider=f.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=f.sharedMesh;colliders.Add(collider);
   }
   Physics.SyncTransforms();
   foreach(float ex in new[]{-.2f,0,.2f})foreach(float ey in new[]{-.1f,0,.1f})foreach(float distance in new[]{.35f,.6f,.95f}){
    var eye=new Vector3(ex,ey,-distance);
    for(int x=0;x<81;x++)for(int y=0;y<45;y++){
     var pixel=new Vector3(.53f*((x+.5f)/81-.5f),.298125f*((y+.5f)/45-.5f),0);
     var direction=(pixel-eye).normalized;rays++;
     if(Physics.Raycast(eye,direction,10,1<<31,QueryTriggerInteraction.Ignore))continue;
     var q=eye+direction*((4.42f*scale-eye.z)/direction.z);
     bool window=Mathf.Abs(q.x)<=1.04f*scale+.00001f&&q.y>=(.755f-1.2f)*scale-.00001f&&q.y<=(1.965f-1.2f)*scale+.00001f;
     if(!window){
      // PhysX cooking may drop a ray exactly on a welded UV seam. Check the
      // actual render triangles before interpreting that as a missing surface.
      bool hit=false;foreach(var f in content.transform.Find("Architecture").GetComponentsInChildren<MeshFilter>())if(RenderTriangleHit(f,eye,direction)){hit=true;break;}
      if(!hit)throw new Exception($"Imported Train enclosure leak: eye={eye}, pixel={x},{y}, rear={q}");
      triangleFallbacks++;continue;
     }
     windows++;
    }
   }
   File.WriteAllText("../Validation/Train/imported-enclosure.txt",$"PASS: {rays} rays across 27 combined eye positions; {windows} intentional window rays; zero enclosure leaks; backfaces disabled; {triangleFallbacks} seam hits verified against render triangles. Full analytic furniture suspension envelope inside volume.\n");
  }finally{
   foreach(var c in colliders)UnityEngine.Object.DestroyImmediate(c);
   foreach(var p in layers)p.Key.layer=p.Value;
   Physics.queriesHitBackfaces=oldBackfaces;
  }
 }
}

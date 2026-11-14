using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Static visibility of a small lit water patch, sampled into receiver vertices.
// This is an art-directed reflected-light approximation, not real-time GI.
public static class LimpoolWaterMask {
 public static void Build(){
  Physics.SyncTransforms();
  var root=GameObject.Find("DynamicWaterLightReceivers");
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
   var original=GameObject.Find(filter.name.Replace("_WaterLight","")).GetComponent<MeshFilter>().sharedMesh;
   var mesh=new Mesh{name=filter.name+"_Visibility"};
   var vertices=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
   bool wall=filter.name.StartsWith("Wall_")||filter.name.Contains("Pier");
   var v=original.vertices;var n=original.normals;var tri=original.triangles;
   void Add(Vector3 p,Vector3 normal){
    float visible=0;
    if(wall&&p.y>1.05f){
     Vector3 end=filter.transform.TransformPoint(p+normal*.015f);
     for(int s=0;s<3;s++){
      Vector3 start=root.transform.TransformPoint(new Vector3(-1.8f+s*.5f,1.14f,3.4f+s*.6f));
      if(!Physics.Linecast(start,end,1,QueryTriggerInteraction.Ignore))visible+=1f/3;
     }
    }
    indices.Add(vertices.Count);vertices.Add(p);normals.Add(normal);colors.Add(new Color(visible,visible,visible,1));
   }
   void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal,int depth){
    if(wall&&depth<7&&Mathf.Max((a-b).sqrMagnitude,Mathf.Max((b-c).sqrMagnitude,(c-a).sqrMagnitude))>.12f){
     var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;
     Triangle(a,ab,ca,normal,depth+1);Triangle(ab,b,bc,normal,depth+1);Triangle(ca,bc,c,normal,depth+1);Triangle(ab,bc,ca,normal,depth+1);
    }else{Add(a,normal);Add(b,normal);Add(c,normal);}
   }
   for(int i=0;i<tri.Length;i+=3)Triangle(v[tri[i]],v[tri[i+1]],v[tri[i+2]],n[tri[i]],0);
   mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
   string path="Assets/Limpool/Meshes/"+mesh.name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(saved){EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,path);
   filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
  }
 }
}

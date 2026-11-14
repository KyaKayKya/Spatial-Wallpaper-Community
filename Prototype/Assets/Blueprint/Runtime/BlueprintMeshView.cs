using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Blueprint {
 // Each object is a spatial/material batch, never one object per block.
 public sealed class BlueprintMeshView:MonoBehaviour {
  readonly List<Mesh> meshes=new List<Mesh>();
  public int RendererCount {get;private set;}
  public void Load(CompiledBlueprint compiled,BlueprintResources resources,bool weatherCollision=false){
   Clear();foreach(var part in compiled.Parts){
    var mesh=new Mesh{name="Blueprint "+part.Name,indexFormat=IndexFormat.UInt16};int count=part.Positions.Length/3;
    var positions=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];var colors=new Color32[count];
    for(int i=0;i<count;i++){positions[i]=new Vector3(part.Positions[i*3],part.Positions[i*3+1],part.Positions[i*3+2]);normals[i]=new Vector3(part.Normals[i*3],part.Normals[i*3+1],part.Normals[i*3+2]);uv[i]=new Vector2(part.Uv[i*2],part.Uv[i*2+1]);colors[i]=new Color32(part.Colors[i*4],part.Colors[i*4+1],part.Colors[i*4+2],part.Colors[i*4+3]);}
    mesh.vertices=positions;mesh.normals=normals;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=part.Indices;mesh.RecalculateBounds();mesh.RecalculateTangents();meshes.Add(mesh);
    var go=new GameObject("Batch "+part.Name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=resources.Material(part.Kind);renderer.shadowCastingMode=part.Kind==BlockRenderKind.Glass||part.Kind==BlockRenderKind.Water?ShadowCastingMode.Off:ShadowCastingMode.On;renderer.receiveShadows=true;
    if(part.Kind==BlockRenderKind.Water)go.layer=4;
    if(weatherCollision&&part.Kind!=BlockRenderKind.Glass&&part.Kind!=BlockRenderKind.Water)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    mesh.UploadMeshData(true);
   }RendererCount=meshes.Count;
  }
  public void Clear(){foreach(Transform child in transform)Destroy(child.gameObject);foreach(var mesh in meshes)if(mesh)Destroy(mesh);meshes.Clear();RendererCount=0;}
  void OnDestroy(){foreach(var mesh in meshes)if(mesh)Destroy(mesh);meshes.Clear();}
 }
}

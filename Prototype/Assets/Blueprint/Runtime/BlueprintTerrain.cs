using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Blueprint {
 // Finite authored surroundings. No world chunks, gameplay or background generation.
 public sealed class BlueprintTerrain:MonoBehaviour {
  readonly List<Mesh> owned=new List<Mesh>();Transform root;Material binding;PoolPlanarReflection reflection;Material water;float groundY;string signature;
  BlueprintScene pendingScene;BlueprintSettings pendingSettings;float rebuildAt;
  public BlueprintFootprint LastFootprint {get;private set;}
  public Rect PondBounds {get;private set;}
  public void Invalidate(){signature=null;pendingScene=null;Clear();}
  void Update(){if(pendingScene&&Time.unscaledTime>=rebuildAt){var scene=pendingScene;var settings=pendingSettings;pendingScene=null;Rebuild(scene,settings);Apply(scene,settings);}}
  void Rebuild(BlueprintScene scene,BlueprintSettings settings){Clear();groundY=scene.Placement.GroundY;LastFootprint=BlueprintFootprint.For(scene.Document,scene.Placement,settings);root=new GameObject("Blueprint surroundings").transform;root.SetParent(transform,false);if(settings.Terrain=="none")return;CreateGround(scene,settings);CreateTrees(scene,settings);}

  public void Apply(BlueprintScene scene,BlueprintSettings settings){
   string next=settings.Terrain+"|"+settings.WaterLevel+"|"+settings.Scale+"|"+settings.Yaw+"|"+settings.OffsetX+"|"+settings.OffsetY+"|"+settings.OffsetZ+"|"+scene.Placement.Scale;
   if(signature!=next){signature=next;if(!root)Rebuild(scene,settings);else{pendingScene=scene;pendingSettings=settings.Clone();rebuildAt=Time.unscaledTime+.15f;}}
   if(reflection){reflection.enabled=settings.Quality>0;reflection.resolutionScale=settings.Quality==2?.5f:.3f;}
   if(water){water.SetVector("_NormalMapASpeeds",settings.WaterMotion?new Vector4(.025f,.012f,-.01f,.016f):Vector4.zero);}
  }
  static void Quad(List<Vector3> v,List<Vector2> uv,List<int> tris,float x0,float z0,float x1,float z1,float y,float tile){int i=v.Count;v.AddRange(new[]{new Vector3(x0,y,z0),new Vector3(x0,y,z1),new Vector3(x1,y,z1),new Vector3(x1,y,z0)});uv.AddRange(new[]{new Vector2(x0*tile,z0*tile),new Vector2(x0*tile,z1*tile),new Vector2(x1*tile,z1*tile),new Vector2(x1*tile,z0*tile)});tris.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});}
  Mesh Make(string name,List<Vector3> v,List<Vector2> uv,List<int> tri){var m=new Mesh{name=name,indexFormat=v.Count>65000?IndexFormat.UInt32:IndexFormat.UInt16};m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(tri,0);var colors=new Color32[v.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,255,255,0);m.colors32=colors;m.RecalculateNormals();m.RecalculateTangents();owned.Add(m);return m;}
  GameObject Draw(string name,Mesh mesh,Material material,bool collider=false){var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;}
  static void Carve(List<Rect> surfaces,Rect cut){var next=new List<Rect>();foreach(var r in surfaces){float x0=Mathf.Max(r.xMin,cut.xMin),x1=Mathf.Min(r.xMax,cut.xMax),z0=Mathf.Max(r.yMin,cut.yMin),z1=Mathf.Min(r.yMax,cut.yMax);if(x1<=x0||z1<=z0){next.Add(r);continue;}if(x0>r.xMin)next.Add(Rect.MinMaxRect(r.xMin,r.yMin,x0,r.yMax));if(x1<r.xMax)next.Add(Rect.MinMaxRect(x1,r.yMin,r.xMax,r.yMax));if(z0>r.yMin)next.Add(Rect.MinMaxRect(x0,r.yMin,x1,z0));if(z1<r.yMax)next.Add(Rect.MinMaxRect(x0,z1,x1,r.yMax));}surfaces.Clear();surfaces.AddRange(next);}
  void Banks(Rect rect,float bottom,float top,BlueprintScene scene){var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();var corners=new[]{new Vector3(rect.xMin,0,rect.yMin),new Vector3(rect.xMin,0,rect.yMax),new Vector3(rect.xMax,0,rect.yMax),new Vector3(rect.xMax,0,rect.yMin)};for(int edge=0;edge<4;edge++){var a=corners[edge];var b=corners[(edge+1)%4];int i=v.Count;v.AddRange(new[]{new Vector3(a.x,bottom,a.z),new Vector3(a.x,top,a.z),new Vector3(b.x,top,b.z),new Vector3(b.x,bottom,b.z)});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});tri.AddRange(new[]{i,i+1,i+2,i,i+2,i+3,i+3,i+2,i+1,i+3,i+1,i});}Draw("Terrain banks",Make("Foundation and water banks",v,uv,tri),scene.ResourcesOwner.Ground("dirt"));}
  void CreateGround(BlueprintScene scene,BlueprintSettings settings){
   float tile=1/scene.Placement.Scale;var f=LastFootprint;bool pond=settings.Terrain=="waterfront"||settings.Terrain=="island",island=settings.Terrain=="island",pit=f.BaseY<groundY-.001f;
   var footprint=Rect.MinMaxRect(Mathf.Clamp(f.MinX-.04f,-5.9f,5.9f),Mathf.Clamp(f.MinZ-.04f,.035f,9.9f),Mathf.Clamp(f.MaxX+.04f,-5.9f,5.9f),Mathf.Clamp(f.MaxZ+.04f,.035f,9.9f));
   PondBounds=Rect.MinMaxRect(island?Mathf.Max(-5.8f,Mathf.Min(-2,f.MinX-.9f)):Mathf.Min(5,f.MaxX+.06f),.05f,island?Mathf.Min(5.8f,Mathf.Max(2,f.MaxX+.9f)):Mathf.Min(5.8f,Mathf.Max(1.5f,f.MaxX+.86f)),Mathf.Min(9.8f,Mathf.Max(island?4:1.2f,f.MaxZ+.3f)));
   var ground=new List<Rect>{Rect.MinMaxRect(-6,0,6,10)};var wet=new List<Rect>();if(pond){Carve(ground,PondBounds);wet.Add(PondBounds);Carve(wet,footprint);if(island)ground.Add(footprint);}if(pit)Carve(ground,footprint);
   var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();foreach(var rect in ground)Quad(v,uv,tri,rect.xMin,rect.yMin,rect.xMax,rect.yMax,groundY,tile);foreach(var rect in wet)Quad(v,uv,tri,rect.xMin,rect.yMin,rect.xMax,rect.yMax,groundY-.12f,tile);if(pit){Quad(v,uv,tri,footprint.xMin,footprint.yMin,footprint.xMax,footprint.yMax,f.BaseY-.015f,tile);Banks(footprint,f.BaseY-.015f,groundY,scene);}Draw("ActualGround",Make("Footprint-aware continuous ground",v,uv,tri),scene.ResourcesOwner.Ground(settings.Terrain=="snow"?"snow":"grass_block_top"),true);
   if(pond&&wet.Count>0){Banks(PondBounds,groundY-.12f,groundY,scene);var wv=new List<Vector3>();var wu=new List<Vector2>();var wt=new List<int>();foreach(var rect in wet)Quad(wv,wu,wt,rect.xMin,rect.yMin,rect.xMax,rect.yMax,0,1);water=scene.ResourcesOwner.Water();var go=Draw("Template water",Make("Footprint-masked water",wv,wu,wt),water);go.transform.localPosition=Vector3.up*(groundY+settings.WaterLevel);go.layer=4;go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;reflection=go.AddComponent<PoolPlanarReflection>();reflection.useSourceBackground=true;reflection.mipFilteredReflection=true;binding=new Material(Resources.Load<Material>("Blueprint/Materials/ReflectionBinding"));reflection.reflectionMaterial=binding;}
  }
  void CreateTrees(BlueprintScene scene,BlueprintSettings settings){
   var tree=new BlueprintDocument(9,13,9);int log=tree.Intern(new BlockState("minecraft:oak_log",new Dictionary<string,string>{{"axis","y"}})),leaf=tree.Intern(new BlockState(settings.Terrain=="snow"?"minecraft:spruce_leaves":"minecraft:oak_leaves"));
   for(int y=0;y<9;y++)tree.Put(4,y,4,log);for(int y=5;y<12;y++){int radius=y<9?3:y<11?2:1;for(int x=4-radius;x<=4+radius;x++)for(int z=4-radius;z<=4+radius;z++){if(Math.Abs(x-4)+Math.Abs(z-4)>radius*1.7f)continue;if(tree.At(x,y,z).IsAir)tree.Put(x,y,z,leaf);}}tree.Finish();
   var compiled=BlockMeshCompiler.Compile(tree,scene.ResourcesOwner.Catalog,CancellationToken.None);var template=new GameObject("Shared tree geometry");template.transform.SetParent(root,false);var source=template.AddComponent<BlueprintMeshView>();source.Load(compiled,scene.ResourcesOwner);template.SetActive(false);
   var points=new[]{new Vector3(-.60f,0,.55f),new Vector3(.67f,0,.96f),new Vector3(-.80f,0,1.42f),new Vector3(.94f,0,1.9f),new Vector3(-1.25f,0,2.1f),new Vector3(1.35f,0,2.7f),new Vector3(-.4f,0,2.4f),new Vector3(.45f,0,2.9f),new Vector3(-1.8f,0,3.1f),new Vector3(1.9f,0,3.4f)};
   int i=0;foreach(var p in points){if(LastFootprint.Contains(p.x,p.z,4*scene.Placement.Scale))continue;if((settings.Terrain=="island"||settings.Terrain=="waterfront")&&PondBounds.Contains(new Vector2(p.x,p.z)))continue;var instance=new GameObject("Tree "+i++).transform;instance.SetParent(root,false);instance.localPosition=new Vector3(p.x,groundY,p.z);instance.localScale=Vector3.one*scene.Placement.Scale;foreach(var filter in template.GetComponentsInChildren<MeshFilter>(true)){var go=new GameObject("Shared tree batch",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(instance,false);go.transform.localPosition=new Vector3(-4,0,-4);go.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;go.GetComponent<MeshRenderer>().sharedMaterial=filter.GetComponent<MeshRenderer>().sharedMaterial;}}
  }
  void Clear(){if(root){root.gameObject.SetActive(false);Destroy(root.gameObject);root=null;}foreach(var m in owned)if(m)Destroy(m);owned.Clear();if(binding)Destroy(binding);binding=null;reflection=null;water=null;}
  void OnDestroy(){Clear();}
 }
}

using UnityEngine;
using SpatialWallpaper.Sdk;

[WallpaperScript]
public sealed class DynamicResourcesSample:MonoBehaviour {
 public GameObject target;
 public Texture2D texture;
 public Mesh mesh;
 public Vector3[] vertices;
 public int frame;
 void Start(){
  mesh=SpatialWallpaper.Sdk.Resources.CreateMesh();
  vertices=new[]{new Vector3(-.09f,-.05f,.18f),new Vector3(0,.07f,.18f),new Vector3(.09f,-.05f,.18f)};
  mesh.vertices=vertices;mesh.triangles=new[]{0,1,2};mesh.RecalculateNormals();mesh.RecalculateBounds();
  SpatialWallpaper.Sdk.Resources.BindMesh(target,mesh);
 }
 void Update(){
  frame=frame+1;if(frame>120)frame=0;
  vertices[1]=new Vector3(0,.04f+frame*.0003f,.18f);mesh.vertices=vertices;mesh.RecalculateBounds();
  texture.SetPixel(frame%4,(frame/4)%4,new Color(frame/120f,.3f,.8f,1));texture.Apply();
 }
 // The Worker releases its owned mesh on unload. A long-lived scene that
 // replaces resources should explicitly call Resources.Release on the old one.
}

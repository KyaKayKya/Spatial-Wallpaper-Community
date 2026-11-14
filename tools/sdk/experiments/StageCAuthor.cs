// Original MIT SDK verification sample.
using UnityEngine;
using System.Collections.Generic;
using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageCAuthor:MonoBehaviour {
 public Transform target;
 public int counter;
 public StageCPeer peer;
 public StageCData data;
 public Prefab template;
 public List<int> items;
 public bool active;
 public List<float> samples;
 public Vector3[] vertices;
 bool Enabled(bool value){return value;}
 void Start(){
  vertices=new Vector3[3];vertices[0]=new Vector3(-1,-1,0);vertices[1]=new Vector3(0,1,0);vertices[2]=new Vector3(1,-1,0);
  var mesh=SpatialWallpaper.Sdk.Resources.CreateMesh();mesh.vertices=vertices;var triangles=new int[3];triangles[0]=0;triangles[1]=1;triangles[2]=2;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.Clear();SpatialWallpaper.Sdk.Resources.Release(mesh);
  var texture=SpatialWallpaper.Sdk.Resources.CreateTexture(16,16);texture.SetPixel(0,0,new Color(0.25f,0.5f,0.75f,1));texture.Apply();SpatialWallpaper.Sdk.Resources.Release(texture);
  active=Enabled(true);counter=data.value;samples=new List<float>(2);samples.Add(.5f);samples[0]=2.5f;}
 void Update(){bool next=!active;active=Enabled(next);counter++;items.Add(counter);peer.value++;target.Rotate(0,1f,0);if(counter==5){samples.Clear();samples.Add(1f);var spawned=Objects.Instantiate(template);Objects.Destroy(spawned);}}
}

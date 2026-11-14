using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;

public static partial class WallpaperSceneBuilder {
 const float ParkingScale=.53f/12f,ParkingFloor=-.298125f/2;
 static Mesh ParkingSourceMesh(JObject data){
  var p=data["vertices"].ToObject<float[]>();var n=data["normals"].ToObject<float[]>();var uv=data["uv"].ToObject<float[]>();
  var vertices=new Vector3[p.Length/3];var normals=new Vector3[vertices.Length];var coords=new Vector2[vertices.Length];
  for(int i=0;i<vertices.Length;i++){vertices[i]=new Vector3(p[i*3],p[i*3+1],p[i*3+2]);if((string)data["name"]=="LampLOD")vertices[i].y=Mathf.Max(0,vertices[i].y);normals[i]=new Vector3(n[i*3],n[i*3+1],n[i*3+2]);coords[i]=new Vector2(uv[i*2],uv[i*2+1]);}
  var mesh=new Mesh{name=(string)data["name"],indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=coords;var subs=(JArray)data["submeshes"];mesh.subMeshCount=subs.Count;
  for(int i=0;i<subs.Count;i++)mesh.SetTriangles(subs[i]["indices"].ToObject<int[]>(),i);mesh.RecalculateBounds();return mesh;
 }
 static void ParkingQuad(List<Vector3> vertices,List<int> indices,Vector3 a,Vector3 b,Vector3 c,Vector3 d){int k=vertices.Count;vertices.AddRange(new[]{a,b,c,d});indices.AddRange(new[]{k,k+2,k+1,k,k+3,k+2});}
 static void ParkingMerged(Transform root,List<Vector3> vertices,List<int> indices,Material material,string name){
  if(vertices.Count==0)return;var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Mesh(root,mesh,Matrix4x4.identity,material,name);UnityEngine.Object.DestroyImmediate(mesh);
 }
 static void Parking(Transform root){
  var data=JObject.Parse(File.ReadAllText(Base+"/Source/Parking/scene.json"));var materials=new Dictionary<string,Material>();
  foreach(JObject item in data["materials"]){
   string name=(string)item["name"];var color=item["color"];var material=Material(name.Contains("lens")?"ParkingSdk/Lamp":"ParkingSdk/Surface","Parking "+materials.Count);
   if(name.Contains("lens"))material.SetColor("_LampEmission",new Color(1.95f,2.4f,3,1));
   else{material.SetColor("_Tint",new Color((float)color[0],(float)color[1],(float)color[2],1).gamma);material.SetFloat("_Textured",0);
    foreach(var texture in item["textures"]){string filename=(string)texture["file"];if(filename.Contains("Albedo")||filename.Contains("_diff")){material.SetTexture("_BaseMap",Texture(filename,128));material.SetColor("_Tint",Color.white);material.SetFloat("_Textured",1);break;}}
   }materials.Add(name,material);
  }
  var transform=Matrix4x4.TRS(new Vector3(0,ParkingFloor,0),Quaternion.identity,Vector3.one*ParkingScale);
  var activeScreens=((JArray)data["screens"]).Select(t=>(string)t["name"]).ToArray();
  foreach(JObject item in ((JArray)data["meshes"]).Where(t=>((string)t["name"]).StartsWith("TV_",StringComparison.Ordinal))){
   string name=(string)item["name"];var mesh=ParkingSourceMesh(item);var mat=materials[(string)item["submeshes"][0]["material"]];int screenIndex=Array.IndexOf(activeScreens,name);
   if(screenIndex>=0){mat=Material("ParkingSdk/CRT","Parking CRT "+screenIndex);mat.SetFloat("_Seed",17+screenIndex*14);mat.SetFloat("_Brightness",1);}
   Mesh(root,mesh,transform,mat,name);UnityEngine.Object.DestroyImmediate(mesh);
  }
  var positions=((JArray)data["lamps"]).Select(t=>new Vector3((float)t["position"][0],0,(float)t["position"][2])).ToArray();
  // Four nearby fixtures reuse the original low-detail geometry; only two
  // merged meshes represent the remaining field. No native instancing API.
  var close=positions.Where(p=>p.z<38&&Mathf.Abs(p.x)<25).OrderBy(p=>p.z+Mathf.Abs(p.x)).Take(4).ToArray();
  var lampData=((JArray)data["meshes"]).Cast<JObject>().Single(t=>(string)t["name"]=="LampLOD");var lamp=ParkingSourceMesh(lampData);
  for(int sub=0;sub<lamp.subMeshCount;sub++){
   var original=Mesh(root,lamp,transform,materials[(string)lampData["submeshes"][sub]["material"]],"Near fixture 0 part "+sub,sub);original.transform.localPosition=close[0]*ParkingScale;
   for(int i=1;i<close.Length;i++){var go=new GameObject("Near fixture "+i+" part "+sub);go.transform.SetParent(root,false);go.transform.localPosition=close[i]*ParkingScale;go.AddComponent<MeshFilter>().sharedMesh=original.GetComponent<MeshFilter>().sharedMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=original.sharedMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
  }UnityEngine.Object.DestroyImmediate(lamp);
  var poles=new List<Vector3>();var poleIndices=new List<int>();var heads=new List<Vector3>();var headIndices=new List<int>();
  foreach(var p in positions){if(close.Contains(p))continue;var origin=p*ParkingScale+new Vector3(0,ParkingFloor,0);float width=.085f*ParkingScale,top=7.25f*ParkingScale;
   ParkingQuad(poles,poleIndices,origin+new Vector3(-width,0,0),origin+new Vector3(width,0,0),origin+new Vector3(width,top,0),origin+new Vector3(-width,top,0));
   // Slightly tilted luminous head remains visible from below and off-axis.
   ParkingQuad(heads,headIndices,origin+new Vector3(-.55f,7.1f,-.42f)*ParkingScale,origin+new Vector3(.55f,7.1f,-.42f)*ParkingScale,origin+new Vector3(.55f,7.32f,.42f)*ParkingScale,origin+new Vector3(-.55f,7.32f,.42f)*ParkingScale);
  }
  ParkingMerged(root,poles,poleIndices,materials["Satin galvanized charcoal steel"],"Distant lamp posts");ParkingMerged(root,heads,headIndices,materials["Frosted lamp lens | physically emissive"],"Distant luminous heads");
  var floor=Material("ParkingSdk/Asphalt","Parking asphalt");floor.SetTexture("_BaseMap",Texture("clean_asphalt_diff_4k_jpg.png",256));floor.SetTexture("_MacroMap",Texture("aerial_asphalt_01_macro_blurred_4k_png.png",128));floor.SetTexture("_WearMap",Texture("Asphalt_Weathering_26m_RGB_png.png",128));
  const float extent=(360*3+80)*ParkingScale,depth=360*ParkingScale;var ground=Quad(new Vector3(-extent,ParkingFloor,0),new Vector3(extent,ParkingFloor,0),new Vector3(extent,ParkingFloor,depth),new Vector3(-extent,ParkingFloor,depth));Mesh(root,ground,Matrix4x4.identity,floor,"Continuous asphalt");UnityEngine.Object.DestroyImmediate(ground);
 }
}

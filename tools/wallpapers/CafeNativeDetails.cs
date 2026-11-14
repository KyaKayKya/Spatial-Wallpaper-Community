using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Extract Unity's original tessellation; a Blender round trip can refill the
// concave window apertures. No decimation or UV reconstruction occurs here.
public static class CafeNativeDetails {
 static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 static Vector3 Vector(JToken token)=>new Vector3((float)token[0],(float)token[1],(float)token[2]);
 public static void Run(){try{
  string output=Environment.GetEnvironmentVariable("SDK_CAFE_DETAILS_OUTPUT");var config=JArray.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("SDK_CAFE_DETAILS_CONFIG")));
  EditorSceneManager.OpenScene("Assets/Library/Scenes/rainycafe.unity");var projection=UnityEngine.Object.FindObjectsOfType<WindowProjection>().First();var frame=projection.screenFrame;var report=new JArray();
  foreach(JObject record in config){string group=(string)record["group"],name=(string)record["name"],source="Assets/RainyCafe/Generated/"+group+".fbx";if(Digest(File.ReadAllBytes(source))!=(string)record["source_sha256"])throw new InvalidDataException("original-detail-source-changed:"+group);
   var boxes=((JArray)record["source_boxes"]).Select(pair=>{var lo=Vector(pair[0]);var hi=Vector(pair[1]);return new Bounds((lo+hi)/2,hi-lo+Vector3.one*.00004f);}).ToArray();
   var positions=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();var uv=new List<Vector2>();var uv2=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();
   foreach(var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>()){
    bool belongs=false;for(var parent=filter.transform;parent!=null;parent=parent.parent)if(parent.name==group){belongs=true;break;}if(!belongs)continue;
    if(AssetDatabase.GetAssetPath(filter.sharedMesh)!=source)throw new InvalidDataException("unexpected-detail-mesh:"+filter.name);
    var mesh=filter.sharedMesh;var p=mesh.vertices;var n=mesh.normals;var t=mesh.tangents;var u=mesh.uv;var u2=mesh.uv2;var c=mesh.colors;var matrix=frame.worldToLocalMatrix*filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;var remap=new Dictionary<int,int>();
    foreach(int sub in Enumerable.Range(0,mesh.subMeshCount)){var triangles=mesh.GetTriangles(sub);for(int at=0;at<triangles.Length;at+=3){var center=matrix.MultiplyPoint3x4((p[triangles[at]]+p[triangles[at+1]]+p[triangles[at+2]])/3);const float scale=.53f/5.4f;var authored=new Vector3(center.x/scale,center.z/scale,center.y/scale+.008f+3.0375f/2);bool selected=group=="Props"?authored.y>4&&authored.z>1.4f:boxes.Any(box=>box.Contains(authored));if(!selected)continue;
     for(int corner=0;corner<3;corner++){int index=triangles[at+corner];if(!remap.TryGetValue(index,out var mapped)){mapped=positions.Count;remap[index]=mapped;positions.Add(matrix.MultiplyPoint3x4(p[index]));normals.Add(n.Length==p.Length?normalMatrix.MultiplyVector(n[index]).normalized:Vector3.up);var tangent=t.Length==p.Length?t[index]:new Vector4(1,0,0,1);var direction=matrix.MultiplyVector(new Vector3(tangent.x,tangent.y,tangent.z)).normalized;tangents.Add(new Vector4(direction.x,direction.y,direction.z,tangent.w*(matrix.determinant<0?-1:1)));uv.Add(u.Length==p.Length?u[index]:Vector2.zero);uv2.Add(u2.Length==p.Length?u2[index]:Vector2.zero);colors.Add(c.Length==p.Length?c[index]:Color.white);}indices.Add(mapped);}
    }}
   }
   if(positions.Count==0)throw new InvalidDataException("original-detail-empty:"+group);byte[] bytes;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(0x314d5753u);writer.Write(positions.Count);writer.Write(1);for(int i=0;i<positions.Count;i++){foreach(float value in new[]{positions[i].x,positions[i].y,positions[i].z,normals[i].x,normals[i].y,normals[i].z,tangents[i].x,tangents[i].y,tangents[i].z,tangents[i].w,uv[i].x,uv[i].y,uv2[i].x,uv2[i].y,colors[i].r,colors[i].g,colors[i].b,colors[i].a})writer.Write(value);}writer.Write(indices.Count);foreach(int index in indices)writer.Write(index);writer.Flush();bytes=stream.ToArray();}
   SpatialWallpaper.Sdk.ResourceData.ValidateMesh(bytes);File.WriteAllBytes(Path.Combine(output,name+".smesh"),bytes);var entry=(JObject)record.DeepClone();entry["output_sha256"]=Digest(bytes);entry["vertices"]=positions.Count;entry["indices"]=indices.Count;entry["coordinate_space"]="original-screen-frame-metres";report.Add(entry);
  }
  File.WriteAllText(Path.Combine(output,"details.json"),report.ToString());EditorApplication.Exit(0);
 }catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
}

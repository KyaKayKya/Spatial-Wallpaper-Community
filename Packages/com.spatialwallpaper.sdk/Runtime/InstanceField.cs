using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Sdk {
 public sealed class InstanceField:MonoBehaviour {
  public Mesh mesh;public Material[] materials;public Vector3[] positions;public float instanceScale=1;
  [System.NonSerialized]public Camera displayCamera;
  Matrix4x4[][] batches;Bounds[] bounds;Matrix4x4 last;readonly Plane[] planes=new Plane[6];
  void Rebuild(){last=transform.localToWorldMatrix;int count=(positions.Length+127)/128;batches=new Matrix4x4[count][];bounds=new Bounds[count];for(int b=0;b<count;b++){int n=Mathf.Min(128,positions.Length-b*128);batches[b]=new Matrix4x4[n];for(int j=0;j<n;j++){var matrix=last*Matrix4x4.TRS(positions[b*128+j],Quaternion.identity,Vector3.one*instanceScale);batches[b][j]=matrix;var e=mesh.bounds.extents;var x=matrix.MultiplyVector(new Vector3(e.x,0,0));var y=matrix.MultiplyVector(new Vector3(0,e.y,0));var z=matrix.MultiplyVector(new Vector3(0,0,e.z));var transformed=new Bounds(matrix.MultiplyPoint3x4(mesh.bounds.center),2*new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));if(j==0)bounds[b]=transformed;else bounds[b].Encapsulate(transformed);}}}
  void LateUpdate(){if(!displayCamera||!mesh||positions==null||materials==null)return;if(batches==null||last!=transform.localToWorldMatrix)Rebuild();GeometryUtility.CalculateFrustumPlanes(displayCamera,planes);for(int b=0;b<batches.Length;b++)if(GeometryUtility.TestPlanesAABB(planes,bounds[b]))for(int s=0;s<mesh.subMeshCount;s++)Graphics.DrawMeshInstanced(mesh,s,materials[s],batches[b],batches[b].Length,null,ShadowCastingMode.Off,false,gameObject.layer,displayCamera,LightProbeUsage.Off);}
 }
}

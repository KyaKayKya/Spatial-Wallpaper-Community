using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Parking {
 public sealed class ParkingLampField:MonoBehaviour {
  public Mesh mesh;
  public Material[] materials;
  public Vector3[] positions;
  public float instanceScale=1;
  const int BatchSize=128;
  Matrix4x4[][] batches;Bounds[] bounds;Matrix4x4 last;
  readonly Plane[] planes=new Plane[6];
  public static Bounds TransformBounds(Bounds source,Matrix4x4 matrix){
   var e=source.extents;var x=matrix.MultiplyVector(new Vector3(e.x,0,0));var y=matrix.MultiplyVector(new Vector3(0,e.y,0));var z=matrix.MultiplyVector(new Vector3(0,0,e.z));
   return new Bounds(matrix.MultiplyPoint3x4(source.center),2*new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));
  }
  void Rebuild(){
   int count=(positions.Length+BatchSize-1)/BatchSize;batches=new Matrix4x4[count][];bounds=new Bounds[count];
   last=transform.localToWorldMatrix;
   for(int b=0;b<count;b++){
    int n=Mathf.Min(BatchSize,positions.Length-b*BatchSize);batches[b]=new Matrix4x4[n];
    for(int j=0;j<n;j++){
     Vector3 p=positions[b*BatchSize+j];batches[b][j]=last*Matrix4x4.TRS(p,Quaternion.identity,Vector3.one*instanceScale);
     var transformed=TransformBounds(mesh.bounds,batches[b][j]);
     if(j==0)bounds[b]=transformed;else bounds[b].Encapsulate(transformed);
    }
   }
  }
  void LateUpdate(){
   if(!mesh||positions==null||materials==null)return;
   if(batches==null||last!=transform.localToWorldMatrix)Rebuild();
   var camera=Camera.main;if(!camera)return;GeometryUtility.CalculateFrustumPlanes(camera,planes);
   for(int b=0;b<batches.Length;b++)if(GeometryUtility.TestPlanesAABB(planes,bounds[b]))
    for(int s=0;s<mesh.subMeshCount;s++)Graphics.DrawMeshInstanced(mesh,s,materials[s],batches[b],batches[b].Length,null,ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off);
  }
 }
}

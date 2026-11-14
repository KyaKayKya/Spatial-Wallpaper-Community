using System;
using UnityEngine;

// Dedicated calibration content. No artwork, tracker, UI or device parameters live here.
public sealed class CalibrationRoom:MonoBehaviour {
 public float depth=.3f;
 public Material surface;
 public float Width{get;private set;}public float Height{get;private set;}
 public Transform Cube{get;private set;}
 public MeshFilter[] Walls{get;private set;}
 Material[] materials;
 float builtDepth;
 void Ensure(){
  if(Walls!=null)return;
  var shader=surface?surface.shader:Shader.Find("Spatial/CalibrationGrid");if(!shader)throw new InvalidOperationException("Missing calibration grid shader");
  Walls=new MeshFilter[5];materials=new Material[6];
  string[] names={"Left wall","Right wall","Top wall","Floor","Back wall"};
  for(int i=0;i<5;i++){
   var go=new GameObject(names[i],typeof(MeshFilter),typeof(MeshRenderer));go.hideFlags=HideFlags.DontSave;go.transform.SetParent(transform,false);
   Walls[i]=go.GetComponent<MeshFilter>();Walls[i].sharedMesh=new Mesh{name=names[i]};
   var material=new Material(shader);materials[i]=material;
   material.SetColor("_BaseColor",Color.black);
   material.SetColor("_LineColor",new Color(.46f,.47f,.49f));go.GetComponent<MeshRenderer>().sharedMaterial=material;
  }
  Cube=GameObject.CreatePrimitive(PrimitiveType.Cube).transform;Cube.name="Equal-sided calibration cube";Cube.SetParent(transform,false);
  Cube.gameObject.hideFlags=HideFlags.DontSave;
  var collider=Cube.GetComponent<Collider>();if(collider){if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);}
  materials[5]=new Material(shader);materials[5].SetColor("_BaseColor",new Color(.82f,.87f,.91f));materials[5].SetColor("_LineColor",new Color(.27f,.39f,.48f));materials[5].SetFloat("_GridSize",.2f);materials[5].SetFloat("_Checker",1);
  Cube.GetComponent<Renderer>().sharedMaterial=materials[5];
 }
 public void SetDimensions(float width,float height){
  Ensure();if(Mathf.Abs(width-Width)<.000001f&&Mathf.Abs(height-Height)<.000001f&&Mathf.Abs(depth-builtDepth)<.000001f)return;
  if(width<.01f||height<.01f||depth<.01f)throw new ArgumentException("INVALID_CALIBRATION_VOLUME");
  Width=width;Height=height;builtDepth=depth;float x=width/2,y=height/2,d=depth;
  Quad(0,new Vector3(-x,-y,0),new Vector3(-x,-y,d),new Vector3(-x,y,d),new Vector3(-x,y,0));
  Quad(1,new Vector3(x,-y,d),new Vector3(x,-y,0),new Vector3(x,y,0),new Vector3(x,y,d));
  Quad(2,new Vector3(-x,y,0),new Vector3(-x,y,d),new Vector3(x,y,d),new Vector3(x,y,0));
  Quad(3,new Vector3(-x,-y,d),new Vector3(-x,-y,0),new Vector3(x,-y,0),new Vector3(x,-y,d));
  Quad(4,new Vector3(-x,-y,d),new Vector3(x,-y,d),new Vector3(x,y,d),new Vector3(-x,y,d));
  for(int i=0;i<5;i++){
   materials[i].SetFloat("_GridSize",Mathf.Min(width,height)/8);
   materials[i].SetFloat("_FadeStart",depth*.025f);materials[i].SetFloat("_FadeEnd",depth);
  }
  float side=Mathf.Min(Mathf.Min(width,height),depth)*.25f;
  Cube.localScale=Vector3.one*side;Cube.localPosition=new Vector3(0,0,depth*.5f);
 }
 void Quad(int index,Vector3 a,Vector3 b,Vector3 c,Vector3 d){
  var mesh=Walls[index].sharedMesh;mesh.Clear();mesh.vertices=new[]{a,b,c,d};mesh.triangles=new[]{0,1,2,0,2,3};
  var points=new[]{a,b,c,d};var uv=new Vector2[4];
  for(int i=0;i<4;i++){var v=points[i];uv[i]=index<2?new Vector2(v.z,v.y):index<4?new Vector2(v.x,v.z):new Vector2(v.x,v.y);}
  mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();
 }
 public Vector3[] Aperture(){return new[]{new Vector3(-Width/2,-Height/2,0),new Vector3(Width/2,-Height/2,0),new Vector3(Width/2,Height/2,0),new Vector3(-Width/2,Height/2,0)};}
 void OnDestroy(){if(materials!=null)foreach(var m in materials)if(m)Destroy(m);if(Walls!=null)foreach(var wall in Walls)if(wall&&wall.sharedMesh)Destroy(wall.sharedMesh);}
}

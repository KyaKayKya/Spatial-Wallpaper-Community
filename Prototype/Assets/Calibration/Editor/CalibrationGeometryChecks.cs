using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class CalibrationGeometryChecks {
 public static void Run(CalibrationRuntime app){
  if(!app.room)throw new Exception("Missing procedural calibration room");
  var room=app.room;var projection=app.projection;var camera=projection.GetComponent<Camera>();
  int count=0;float maxError=0;
  foreach(float aspect in new[]{16f/9,16f/10,4f/3,21f/9,9f/16})foreach(float width in new[]{.53f,.265f}){
   float height=width/aspect;room.SetDimensions(width,height);projection.displayWidth=width;projection.displayHeight=height;
   if((room.Cube.localScale-Vector3.one*room.Cube.localScale.x).sqrMagnitude>1e-12)throw new Exception("Cube stretched");count++;
   if((room.Cube.localPosition-new Vector3(0,0,room.depth/2)).sqrMagnitude>1e-12)throw new Exception("Calibration cube must be at the room center");count++;
   for(int face=0;face<5;face++){
    var mesh=room.Walls[face].sharedMesh;
    for(int v=0;v<4;v++){
     var position=mesh.vertices[v];var expected=face<2?new Vector2(position.z,position.y):face<4?new Vector2(position.x,position.z):new Vector2(position.x,position.y);
     if((mesh.uv[v]-expected).sqrMagnitude>1e-12)throw new Exception("Opposite wall grid phases must share centered room coordinates");count++;
    }
   }
   var corners=room.Aperture();
   var vertices=room.Walls.SelectMany(w=>w.sharedMesh.vertices.Select(v=>projection.screenFrame.InverseTransformPoint(w.transform.TransformPoint(v)))).ToArray();
   foreach(var corner in corners){if(!vertices.Any(v=>(v-corner).sqrMagnitude<1e-12))throw new Exception("Actual mesh misses aperture corner");count++;}
   var floor=room.Walls[3].sharedMesh.vertices;
   if(floor.Any(v=>Mathf.Abs(v.y+height/2)>1e-6)||!floor.Any(v=>v.z==0&&v.x==-width/2)||!floor.Any(v=>v.z==0&&v.x==width/2))throw new Exception("Floor misses lower edge");count++;
   foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(-width/2,0,-.6f),new Vector3(width/2,0,-.6f),new Vector3(0,height/2,-.6f),new Vector3(0,-height/2,-.6f),new Vector3(0,0,-.15f),new Vector3(0,0,-1.2f)}){
    if(!projection.SetEye(eye))throw new Exception("Legal eye rejected");
    for(int i=0;i<4;i++){
     var point=camera.WorldToViewportPoint(room.transform.TransformPoint(corners[i]));var expected=new Vector2(i==1||i==2?1:0,i>=2?1:0);
     float error=Vector2.Scale(new Vector2(point.x,point.y)-expected,new Vector2(1600,900)).magnitude;
     maxError=Mathf.Max(maxError,error);if(error>.01f)throw new Exception("Actual aperture corner moved: "+error);count++;
    }
   }
   foreach(int index in new[]{0,1,2,3}){
    var eye=new Vector3(index==0?-width/2:index==1?width/2:0,index==2?height/2:index==3?-height/2:0,-.6f);projection.SetEye(eye);
    var wall=room.Walls[index];var points=wall.sharedMesh.vertices.Select(v=>camera.WorldToViewportPoint(wall.transform.TransformPoint(v))).ToArray();
    Vector2 a=points[1]-points[0],b=points[2]-points[0],c=points[3]-points[0];
    float area=Mathf.Abs(a.x*b.y-a.y*b.x)+Mathf.Abs(b.x*c.y-b.y*c.x);
    if(area>1e-6)throw new Exception("Edge-on wall remains visible: "+index+" area="+area);count++;
   }
  }
  room.SetDimensions(.53f,.298125f);projection.displayWidth=.53f;projection.displayHeight=.298125f;projection.SetEye(new Vector3(0,0,-.6f));
  File.WriteAllText("../Validation/Calibration/geometry.txt","PASS "+count+" actual mesh/cube/aperture/edge-on-wall checks; 5 aspects x 2 sizes x 7 eyes; maximum aperture error="+maxError+" px. Human perspective accuracy not measured.");
 }
}

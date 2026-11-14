using System;
using UnityEngine;
public static class ShowcaseChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);}
 public static string Run(){
  var go=new GameObject("ProjectionTest");var frame=new GameObject("RigidScreenTest");
  try{
   var cam=go.AddComponent<Camera>();var p=go.AddComponent<WindowProjection>();
   var type=p.GetType();var f=type.GetField("screenFrame");
   Check(f!=null,"rigid screen-frame contract missing");
   f.SetValue(p,frame.transform);
   int count=0;
   foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(12,27,-8)}){
    frame.transform.SetPositionAndRotation(new Vector3(.8f,-.3f,1.2f),rotation);
    foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,.1f,-.4f),new Vector3(-.18f,-.08f,-.9f)}){
     Check(p.SetEye(eye),"valid eye rejected");
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      var pt=frame.transform.TransformPoint(new Vector3(x*p.displayWidth,y*p.displayHeight,0));
      var q=cam.WorldToViewportPoint(pt);
      Check(Vector2.Distance(new Vector2(q.x,q.y),new Vector2(x+.5f,y+.5f))<.00002f,"aperture corner drift");count++;
     }
     foreach(float depth in new[]{.035f,.10f,.18f}){
      var point=new Vector3(.031f,-.017f,depth);
      var q=cam.WorldToViewportPoint(frame.transform.TransformPoint(point));
      float d=-eye.z;var projected=(d*new Vector2(point.x,point.y)+depth*new Vector2(eye.x,eye.y))/(d+depth);
      Check(Mathf.Abs(q.x-(.5f+projected.x/p.displayWidth))<.00002f&&Mathf.Abs(q.y-(.5f+projected.y/p.displayHeight))<.00002f,"analytic parallax mismatch");count++;
     }
    }
   }
   var matrix=cam.projectionMatrix;var position=cam.transform.position;
   Check(!p.SetEye(new Vector3(float.NaN,0,-.6f))&&!p.SetEye(new Vector3(0,0,.1f)),"invalid eye accepted");
   Check(matrix==cam.projectionMatrix&&position==cam.transform.position,"invalid input changed view");
   Check(p.ApplyTrackedSample(Vector3.zero,100),"first tracker sample rejected");
   matrix=cam.projectionMatrix;
   Check(!p.ApplyTrackedSample(Vector3.right,99),"out-of-order sample accepted");
   p.trackerToScreen=new Quaternion(0,0,0,0);
   Check(!p.ApplyTrackedSample(Vector3.right,101),"zero calibration quaternion accepted");
   Check(matrix==cam.projectionMatrix,"invalid tracking changed view");
   return "PASS: "+count+" independent corner/depth checks, rotated screen frame, invalid input freeze and timestamp ordering";
  }finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(frame);}
 }
}

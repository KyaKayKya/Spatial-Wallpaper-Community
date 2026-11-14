// Original MIT SDK test fixture; trusted host geometry, never author code.
using System;using System.IO;using UnityEngine;using UnityEngine.Rendering.Universal;
public static class StageCVisuals {
 public static Camera Create(Transform target){
  var view=new GameObject("Trusted camera").AddComponent<Camera>();view.transform.position=new Vector3(0,0,-4);
  view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.02f,.04f,.08f);view.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);marker.transform.SetParent(target,false);
  var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.color=new Color(.1f,.7f,.9f);marker.GetComponent<Renderer>().sharedMaterial=material;
  // Asymmetric accent detects vertical/horizontal flips in pixel transports.
  var accent=GameObject.CreatePrimitive(PrimitiveType.Sphere);accent.transform.SetParent(target,false);
  accent.transform.localPosition=new Vector3(.35f,.7f,0);accent.transform.localScale=Vector3.one*.16f;
  var accentMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));accentMaterial.color=new Color(1,.2f,.1f);accent.GetComponent<Renderer>().sharedMaterial=accentMaterial;
  return view;
 }
 public static void Capture(Camera view,string path){
  var previous=RenderTexture.active;var target=view.targetTexture;var rt=new RenderTexture(640,360,24);var image=new Texture2D(640,360,TextureFormat.RGB24,false);
  try{view.targetTexture=rt;view.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
  finally{view.targetTexture=target;RenderTexture.active=previous;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);}
 }
}

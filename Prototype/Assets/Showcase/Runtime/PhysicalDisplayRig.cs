using UnityEngine;
using System;
using System.IO;
[DefaultExecutionOrder(-300)]
public sealed class PhysicalDisplayRig:MonoBehaviour {
 [Serializable] public sealed class Profile {
  public float widthMetres=.53f,heightMetres=.298125f;
  public Vector3 referenceEyeMetres=new Vector3(0,0,-.6f);
  public bool measured;
 }
 public WindowProjection projection;
 public Transform sceneModel;
 public bool IsMeasured{get;private set;}
 void Awake(){
  string path=Path.Combine(Application.streamingAssetsPath,"display-profile.json");
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--display-profile");
  if(index>=0&&index+1<args.Length)path=args[index+1];
  if(!File.Exists(path))return;
  try{if(!Configure(JsonUtility.FromJson<Profile>(File.ReadAllText(path))))Debug.LogWarning("Invalid display profile; nominal 16:9 preview retained.",this);}
  catch(Exception e){Debug.LogWarning("Display profile could not be read: "+e.Message,this);}
 }
 // This cabinet is authored for 16:9. Uniform scaling preserves baked light transport.
 // Other aspect ratios require a matching cabinet variant, not a stretched sculpture.
 public bool Configure(Profile p){
  if(p==null||!projection||!sceneModel||float.IsNaN(p.widthMetres)||float.IsNaN(p.heightMetres)||
   p.widthMetres<.05f||p.widthMetres>2||p.heightMetres<.05f||p.heightMetres>2||
   Mathf.Abs(p.widthMetres/p.heightMetres-16f/9f)>.005f)return false;
  float oldW=projection.displayWidth,oldH=projection.displayHeight;
  projection.displayWidth=p.widthMetres;projection.displayHeight=p.heightMetres;
  if(!projection.SetEye(p.referenceEyeMetres)){projection.displayWidth=oldW;projection.displayHeight=oldH;return false;}
  projection.referenceEye=p.referenceEyeMetres;sceneModel.localScale=Vector3.one*(p.widthMetres/.53f);
  IsMeasured=p.measured;return true;
 }
}

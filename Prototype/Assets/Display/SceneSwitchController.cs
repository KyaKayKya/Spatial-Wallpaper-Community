using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

// Scene selection uses the same public display rig in every content scene.
public sealed class SceneSwitchController:MonoBehaviour {
 Vector3 eye;bool preserve;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Install(){if(!FindObjectOfType<LibraryPreviewController>()&&SceneManager.sceneCountInBuildSettings>1){var go=new GameObject("SceneSelection");DontDestroyOnLoad(go);go.AddComponent<SceneSwitchController>();}}
 void OnEnable(){SceneManager.sceneLoaded+=Loaded;}
 void OnDisable(){SceneManager.sceneLoaded-=Loaded;}
 void Loaded(Scene scene,LoadSceneMode mode){if(preserve){var p=FindObjectOfType<WindowProjection>();if(p)p.SetEye(eye);preserve=false;}}
 void Update(){if(Input.GetKeyDown(KeyCode.Tab))SwitchTo((SceneManager.GetActiveScene().buildIndex+1)%SceneManager.sceneCountInBuildSettings);}
 public void SwitchTo(int index){var p=FindObjectOfType<WindowProjection>();if(p){eye=p.EyeScreen;preserve=true;}SceneManager.LoadScene(index,LoadSceneMode.Single);}
 IEnumerator Start(){
  var args=Environment.GetCommandLineArgs();int selection=Array.IndexOf(args,"--scene");
  if(selection>=0&&selection+1<args.Length&&args[selection+1]=="showcase")SwitchTo(1);
  int index=Array.IndexOf(args,"--scene-cycle-output");if(index<0||index+1>=args.Length)yield break;
  string output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);
  var report=new System.Text.StringBuilder();
  foreach(int target in new[]{0,1,0}){
   SwitchTo(target);yield return null;yield return new WaitForSeconds(2);
   var p=FindObjectOfType<WindowProjection>();
   bool unique=FindObjectsOfType<WindowProjection>().Length==1&&p;
   int interval=OnDemandRendering.renderFrameInterval;
   bool cadence=target==0?interval==1:interval==6;
   int reflections=0;foreach(var cam in Resources.FindObjectsOfTypeAll<Camera>())if(cam.name=="PoolReflectionCamera")reflections++;
   bool released=target==0?reflections==1:reflections==0;
   bool materials=true;foreach(var renderer in FindObjectsOfType<MeshRenderer>())foreach(var m in renderer.sharedMaterials)if(!m||!m.shader||!m.shader.isSupported)materials=false;
   report.AppendLine($"scene={SceneManager.GetActiveScene().name}; unique={unique}; interval={interval}; reflectionCameras={reflections}; pipeline={QualitySettings.renderPipeline.name}");
   if(!unique||!cadence||!released||!materials){File.WriteAllText(Path.Combine(output,"cycle.txt"),"FAIL\n"+report+"materials="+materials);Application.Quit(1);yield break;}
   yield return new WaitForEndOfFrame();
   var active=RenderTexture.active;RenderTexture.active=null;
   var texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);texture.Apply();RenderTexture.active=active;
   File.WriteAllBytes(Path.Combine(output,SceneManager.GetActiveScene().name+".png"),texture.EncodeToPNG());Destroy(texture);
  }
  File.WriteAllText(Path.Combine(output,"cycle.txt"),"PASS\n"+report);Application.Quit();
 }
}

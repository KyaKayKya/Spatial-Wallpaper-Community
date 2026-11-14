using System;
using System.IO;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Creates new procedural assets and scenes; never overwrites an author scene.
public static class SdkSamples {
 [MenuItem("Spatial Wallpaper/Samples/1 Interaction and storage")]
 public static void InputStorage(){Create("InputStorage");}
 [MenuItem("Spatial Wallpaper/Samples/2 Audio response")]
 public static void Audio(){Create("Audio");}
 [MenuItem("Spatial Wallpaper/Samples/3 Dynamic mesh and texture")]
 public static void DynamicResources(){Create("DynamicResources");}
 [MenuItem("Spatial Wallpaper/Samples/4 Shader and Prefab")]
 public static void ShaderPrefab(){Create("ShaderPrefab");}
 [MenuItem("Spatial Wallpaper/Samples/5 LLM reply and cancellation")]
 public static void LlmReply(){Create("LlmReply");}

 public static SceneDefinition Create(string kind){
  if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return null;
  if(PlayerSettings.colorSpace!=ColorSpace.Linear)throw new InvalidOperationException("Set Player > Other Settings > Color Space to Linear before creating an SDK sample.");
  if(Array.IndexOf(new[]{"InputStorage","Audio","DynamicResources","ShaderPrefab","LlmReply"},kind)<0)throw new ArgumentException("unknown sample");
  Directory.CreateDirectory("Assets/SpatialWallpaperSamples");AssetDatabase.Refresh();
  string folder=AssetDatabase.GenerateUniqueAssetPath("Assets/SpatialWallpaperSamples/"+kind);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  if(!GraphicsSettings.currentRenderPipeline){
   TrustedRenderProfileBuilder.Install(folder);
  }
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.skybox=null;
  var owner=new GameObject("SDK "+kind);var definition=owner.AddComponent<SceneDefinition>();definition.templateId=Guid.NewGuid().ToString("D");definition.displayName="SDK "+kind;
  definition.licenseName="Internal SDK RC validation";definition.licenseSource="Original procedural Spatial Wallpaper SDK starter scene";definition.licenseAttribution="Spatial Wallpaper";
  definition.contentRoot=new GameObject("Content").transform;
  var camera=new GameObject("Author preview camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.06f,1);
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.displayWidth=definition.authorFrame.x;projection.displayHeight=definition.authorFrame.y;projection.contentDepth=definition.authorFrame.z;projection.referenceEye=new Vector3(0,0,-.67f);projection.SetEye(projection.referenceEye);
  var floor=Primitive("Floor",definition.contentRoot,new Vector3(0,-definition.authorFrame.y/2-.1f,1),new Vector3(3,.2f,2));
  var floorMaterial=Material("Universal Render Pipeline/Unlit",new Color(.07f,.11f,.15f,1),folder+"/Floor.mat");floor.GetComponent<MeshRenderer>().sharedMaterial=floorMaterial;
  var target=Primitive("Sample object",definition.contentRoot,new Vector3(0,-.015f,.18f),new Vector3(.11f,.11f,.11f));
  var surface=Material(kind=="ShaderPrefab"?"SpatialWallpaper/Samples/Pulse":"Universal Render Pipeline/Unlit",new Color(.1f,.4f,.9f,1),folder+"/Surface.mat");target.GetComponent<MeshRenderer>().sharedMaterial=surface;
  switch(kind){
   case "InputStorage":owner.AddComponent<InputStorageSample>().surface=surface;break;
   case "Audio":owner.AddComponent<AudioSample>().surface=surface;break;
   case "DynamicResources":
    target.transform.localPosition=Vector3.zero;target.transform.localScale=Vector3.one;
    var texture=new Texture2D(4,4,TextureFormat.RGBA32,false,true);var pixels=new Color[16];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(.2f,.6f,.9f,1);texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,folder+"/Dynamic.asset");surface.SetTexture("_BaseMap",texture);
    var dynamic=owner.AddComponent<DynamicResourcesSample>();dynamic.target=target;dynamic.texture=texture;break;
   case "ShaderPrefab":
    var source=Primitive("Spawned cube",null,new Vector3(.13f,.015f,.25f),new Vector3(.055f,.055f,.055f));source.GetComponent<MeshRenderer>().sharedMaterial=surface;
    var prefab=PrefabUtility.SaveAsPrefabAsset(source,folder+"/Cube.prefab");UnityEngine.Object.DestroyImmediate(source);
    var shader=owner.AddComponent<ShaderPrefabSample>();shader.surface=surface;shader.prefab=new Prefab{source=prefab};break;
   case "LlmReply":owner.AddComponent<LlmReplySample>().surface=surface;break;
  }
  // Scripts live on the content owner so the exporter includes them.
  definition.contentRoot.SetParent(owner.transform,false);definition.contentRoot=owner.transform;
  EditorSceneManager.SaveScene(scene,folder+"/Author.unity");AssetDatabase.SaveAssets();Selection.activeGameObject=owner;return definition;
 }
 static GameObject Primitive(string name,Transform parent,Vector3 position,Vector3 scale){var item=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());item.name=name;if(parent)item.transform.SetParent(parent,false);item.transform.localPosition=position;item.transform.localScale=scale;return item;}
 static Material Material(string shaderName,Color color,string path){var shader=Shader.Find(shaderName);if(!shader)throw new InvalidOperationException("Sample shader missing: "+shaderName);var material=new Material(shader);material.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(material,path);return material;}
}

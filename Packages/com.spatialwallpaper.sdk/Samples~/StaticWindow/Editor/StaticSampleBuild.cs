using System;
using System.IO;
using System.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// This sample depends only on the SDK. It neither installs nor starts Tracker.
public static class StaticSampleBuild {
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SDK_BUILD_OUTPUT");
  if(string.IsNullOrEmpty(output))throw new InvalidOperationException("Set SDK_BUILD_OUTPUT to the export directory");
  CheckResourceReuse(output);
  Directory.CreateDirectory("Assets/Generated");
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
  var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/Generated/Renderer.asset");var pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/Generated/Pipeline.asset");UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var definition=new GameObject("Author").AddComponent<SceneDefinition>();definition.contentRoot=new GameObject("Content").transform;
  var texture=new Texture2D(4,4,TextureFormat.RGBAHalf,false,true);var colors=new Color[16];for(int i=0;i<colors.Length;i++)colors[i]=new Color(.8f,.65f,.5f,1);texture.SetPixels(colors);texture.Apply();AssetDatabase.CreateAsset(texture,"Assets/Generated/SampleLightmap.asset");
  LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=texture}};
  LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());floor.name="Floor";floor.transform.SetParent(definition.contentRoot);floor.transform.localPosition=new Vector3(0,-1.1490625f,1);floor.transform.localScale=new Vector3(3,2,2);
  var lit=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.2f,.35f,.4f),enableInstancing=true};AssetDatabase.CreateAsset(lit,"Assets/Generated/Floor.mat");floor.GetComponent<MeshRenderer>().sharedMaterial=lit;floor.GetComponent<MeshRenderer>().lightmapIndex=0;floor.GetComponent<MeshRenderer>().lightmapScaleOffset=new Vector4(1,1,0,0);
  var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());cube.name="ExternalShaderCube";cube.transform.SetParent(definition.contentRoot);cube.transform.localPosition=new Vector3(0,-.05f,.2f);cube.transform.localScale=Vector3.one*.12f;
  var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/SampleExternal.shader");if(!shader)throw new InvalidOperationException("Copy SampleExternal.shader to Assets before running the sample");
  var material=new Material(shader);material.SetColor("_Color",new Color(1,.35f,.05f));material.SetTexture("_Texture",texture);material.SetTextureScale("_Texture",new Vector2(2,3));material.SetTextureOffset("_Texture",new Vector2(.125f,.25f));AssetDatabase.CreateAsset(material,"Assets/Generated/Cube.mat");cube.GetComponent<MeshRenderer>().sharedMaterial=material;
  var reflection=new Cubemap(4,TextureFormat.RGBAHalf,true){anisoLevel=4};for(int mip=0;mip<3;mip++)for(int face=0;face<6;face++){int size=4>>mip;var pixels=new Color[size*size];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(face/10f,0,.5f-mip*.1f,1);reflection.SetPixels(pixels,(CubemapFace)face,mip);}reflection.Apply(false);AssetDatabase.CreateAsset(reflection,"Assets/Generated/Reflection.asset");material.SetTexture("_Cube",reflection);
  EditorSceneManager.SaveScene(scene,"Assets/Generated/Author.unity");AssetDatabase.SaveAssets();Directory.CreateDirectory(output);
  string id=ScenePackageExporter.Export(definition,Path.Combine(output,"sample.spatial"),ExportMode.LocalValidation);File.WriteAllText(Path.Combine(output,"author-build-id.txt"),id);
  var package=SpatialWallpaper.Content.PackageValidator.Validate(Path.Combine(output,"sample.spatial"));var encodedMaterial=((Newtonsoft.Json.Linq.JArray)package.Scene["materials"]).Single(m=>(string)m["shader"]=="SdkSample/External");int textureIndex=(int)encodedMaterial["properties"]["_Texture"]["value"];var bytes=package.Read((string)package.Scene["textures"][textureIndex]);var decoded=new Texture2D(4,4,(TextureFormat)BitConverter.ToInt32(bytes,12),false,true);try{decoded.LoadRawTextureData(bytes.Skip(44).ToArray());decoded.Apply();var pixel=decoded.GetPixel(0,0);bool pixels=Mathf.Abs(pixel.r-.8f)<.001f&&Mathf.Abs(pixel.g-.65f)<.001f&&Mathf.Abs(pixel.b-.5f)<.001f;File.WriteAllText(Path.Combine(output,"reports/sample-texture-pixels.json"),new Newtonsoft.Json.Linq.JObject{["passed"]=pixels,["actual"]=new Newtonsoft.Json.Linq.JArray(pixel.r,pixel.g,pixel.b)}.ToString());if(!pixels)throw new InvalidOperationException("sample-texture-pixel-roundtrip-failed");}finally{UnityEngine.Object.DestroyImmediate(decoded);}
  LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=texture,lightmapDir=texture}};LightmapSettings.lightmapsMode=LightmapsMode.CombinedDirectional;
  ScenePackageExporter.Export(definition,Path.Combine(output,"directional.spatial"),ExportMode.LocalValidation);
 }
 static void CheckResourceReuse(string output){
  Directory.CreateDirectory(output);var results=new Newtonsoft.Json.Linq.JArray();
  foreach(string kind in new[]{"mesh","texture"}){
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var definition=new GameObject("ReuseAuthor").AddComponent<SceneDefinition>();definition.contentRoot=new GameObject("ReuseContent").transform;
   LightmapSettings.lightmaps=new LightmapData[0];LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional;
   var first=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(first.GetComponent<Collider>());first.transform.SetParent(definition.contentRoot);
   var second=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(second.GetComponent<Collider>());second.transform.SetParent(definition.contentRoot);
   var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));first.GetComponent<MeshRenderer>().sharedMaterial=material;second.GetComponent<MeshRenderer>().sharedMaterial=material;
   Mesh clone=null;Texture2D texture=null,other=null;Material copy=null;
   if(kind=="mesh"){clone=UnityEngine.Object.Instantiate(first.GetComponent<MeshFilter>().sharedMesh);second.GetComponent<MeshFilter>().sharedMesh=clone;}
   else{texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);texture.SetPixels(new[]{Color.red,Color.blue,Color.green,Color.white});texture.Apply();other=UnityEngine.Object.Instantiate(texture);material.SetTexture("_BaseMap",texture);copy=new Material(material);copy.SetTexture("_BaseMap",other);second.GetComponent<MeshRenderer>().sharedMaterial=copy;}
   try{string path=Path.Combine(output,"reuse-"+kind+".spatial");ScenePackageExporter.Export(definition,path,ExportMode.LocalValidation);var data=SpatialWallpaper.Content.PackageValidator.Validate(path).Scene;var list=(Newtonsoft.Json.Linq.JArray)data[kind=="mesh"?"meshes":"textures"];if(list.Count!=1)throw new InvalidOperationException("identical resources must share one index");results.Add(new Newtonsoft.Json.Linq.JObject{["name"]=kind+"-distinct-objects-identical-bytes",["passed"]=true});}
   catch(Exception e){results.Add(new Newtonsoft.Json.Linq.JObject{["name"]=kind+"-distinct-objects-identical-bytes",["passed"]=false,["error"]=e.ToString()});}
   finally{UnityEngine.Object.DestroyImmediate(material);if(copy)UnityEngine.Object.DestroyImmediate(copy);if(clone)UnityEngine.Object.DestroyImmediate(clone);if(texture)UnityEngine.Object.DestroyImmediate(texture);if(other)UnityEngine.Object.DestroyImmediate(other);}
  }
  File.WriteAllText(Path.Combine(output,"reports","author-resource-reuse.json"),results.ToString());foreach(var result in results)if(!(bool)result["passed"])throw new InvalidOperationException("author-resource-reuse-failed");
 }
}

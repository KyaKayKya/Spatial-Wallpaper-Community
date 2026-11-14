using System;using System.IO.Compression;using System.Text;using UnityEditor;using SpatialWallpaper.Sdk;using System.IO;using System.Linq;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class StageFStaticExport {
 static void Need(bool yes,string reason){if(!yes)throw new Exception(reason);}
 public static void Run(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Original static export fixture");var child=new GameObject("Textured triangle");child.transform.SetParent(root.transform,false);
  var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.15f,-.1f,0),new Vector3(0,.1f,0),new Vector3(.15f,-.1f,0)};mesh.triangles=new[]{0,1,2};mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.right};mesh.RecalculateNormals();child.AddComponent<MeshFilter>().sharedMesh=mesh;
  var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);texture.SetPixels(new[]{Color.red,Color.green,Color.blue,Color.white});texture.Apply();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",Color.white);material.SetTexture("_BaseMap",texture);child.AddComponent<MeshRenderer>().sharedMaterial=material;
  var licenses=new JArray(new JObject{{"name","Static resource fixture"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});Func<byte[]> export=()=>ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),licenses);
  byte[] bytes=export();var admitted=Admission.Admit(bytes,"windows-x86_64");string output=Environment.GetEnvironmentVariable("SDK_F_OUTPUT");File.WriteAllBytes(Path.Combine(output,"scene.spatial"),bytes);
  Need(admitted.CapabilityTags.SequenceEqual(new[]{"view-data"}),"static-capability-closure");
  var rejected=new JArray();var accepted=new JArray();Action<string,Action,Action> reject=(name,change,undo)=>{change();try{bool denied=false;try{export();}catch(InvalidDataException){denied=true;}if(denied)rejected.Add(name);else accepted.Add(name);}finally{undo();export();}};
  BoxCollider collider=null;reject("unknown-component",()=>collider=child.AddComponent<BoxCollider>(),()=>UnityEngine.Object.DestroyImmediate(collider));
  reject("multiple-materials",()=>child.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,material},()=>child.GetComponent<MeshRenderer>().sharedMaterials=new[]{material});
  reject("material-keywords",()=>material.EnableKeyword("_ALPHATEST_ON"),()=>material.DisableKeyword("_ALPHATEST_ON"));
  reject("material-texture-transform",()=>material.SetTextureScale("_BaseMap",new Vector2(2,1)),()=>material.SetTextureScale("_BaseMap",Vector2.one));
  reject("renderer-property-block",()=>{var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",Color.magenta);child.GetComponent<MeshRenderer>().SetPropertyBlock(block);},()=>child.GetComponent<MeshRenderer>().SetPropertyBlock(null));
  reject("renderer-force-off",()=>child.GetComponent<MeshRenderer>().forceRenderingOff=true,()=>child.GetComponent<MeshRenderer>().forceRenderingOff=false);
  string pass=material.GetPassName(0);reject("material-disabled-pass",()=>material.SetShaderPassEnabled(pass,false),()=>material.SetShaderPassEnabled(pass,true));
  reject("texture-mixed-wrap",()=>{texture.wrapModeU=TextureWrapMode.Repeat;texture.wrapModeV=TextureWrapMode.Clamp;},()=>texture.wrapMode=TextureWrapMode.Repeat);
  File.WriteAllText(Path.Combine(output,"rejections.json"),new JObject{{"rejected",rejected},{"incorrectly_accepted",accepted}}.ToString());Need(accepted.Count==0,"static-export-accepted:"+accepted);
  string assetRoot="Assets/ResourceFixture"+Environment.GetEnvironmentVariable("SDK_F_RUN");AssetDatabase.CreateFolder("Assets",Path.GetFileName(assetRoot));AssetDatabase.CreateAsset(mesh,assetRoot+"/mesh.asset");AssetDatabase.CreateAsset(texture,assetRoot+"/texture.asset");AssetDatabase.CreateAsset(material,assetRoot+"/material.mat");var prefab=PrefabUtility.SaveAsPrefabAsset(child,assetRoot+"/triangle.prefab");
  var author=root.AddComponent<StageFResourceAuthor>();author.mesh=mesh;author.material=material;author.texture=texture;author.prefab=new Prefab{source=prefab};
  byte[] withReferences=export();Admission.Admit(withReferences,"windows-x86_64");File.WriteAllBytes(Path.Combine(output,"references.spatial"),withReferences);
  using(var archive=new ZipArchive(new MemoryStream(withReferences),ZipArchiveMode.Read))using(var reader=new StreamReader(archive.GetEntry("content.json").Open(),Encoding.UTF8)){
   var content=JObject.Parse(reader.ReadToEnd());var resources=(JArray)content["resources"];Need(resources.Count==3&&((JArray)content["prefabs"]).Count==1,"shared-prefab-resource-inventory");
   var ids=resources.Cast<JObject>().ToDictionary(r=>(string)r["kind"],r=>(string)r["id"]);var components=(JArray)content["nodes"][1]["components"];var renderer=components.Cast<JObject>().Single(c=>(string)c["kind"]=="mesh-renderer");
   var prefabRenderer=((JArray)content["prefabs"][0]["nodes"][0]["components"]).Cast<JObject>().Single(c=>(string)c["kind"]=="mesh-renderer");Need(JToken.DeepEquals(renderer["config"],prefabRenderer["config"]),"prefab-resource-identity-drift");
   var fields=(JObject)((JArray)content["nodes"][0]["components"]).Cast<JObject>().Single(c=>(string)c["kind"]=="script")["fields"];Need(fields.Properties().Select(p=>(string)p.Value).OrderBy(x=>x).SequenceEqual(ids.Values.Concat(new[]{"p0"}).OrderBy(x=>x)),"script-resource-field-reference-drift");
  }
  File.WriteAllText(Path.Combine(output,"author.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_F_RUN")},{"build",admitted.BuildId},{"capabilities",new JArray(admitted.CapabilityTags)},{"rejected",rejected},{"engine",Application.unityVersion}}.ToString());
 }
}

using System.IO;using UnityEditor;using UnityEngine;
public static partial class WallpaperSceneBuilder {
 static Material ShowcaseSurface(string name){
  var material=Material("ShowcaseSdk/BakedDisplaySurface",name);
  string path=Base+"/Source/Showcase/"+name+"_occupied.exr";
  var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)throw new InvalidDataException("showcase-radiance-missing:"+path);
  importer.sRGBTexture=false;importer.maxTextureSize=1024;importer.mipmapEnabled=false;var platform=importer.GetPlatformTextureSettings("Standalone");platform.overridden=true;platform.maxTextureSize=1024;platform.format=TextureImporterFormat.BC6H;importer.SetPlatformTextureSettings(platform);importer.SaveAndReimport();
  material.SetTexture("_Radiance",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
  var lut=Texture("AcceptedAgX.png",1024,true);var li=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut));li.mipmapEnabled=false;li.wrapMode=TextureWrapMode.Clamp;li.SaveAndReimport();material.SetTexture("_AgXLut",lut);
  string reflection=Base+"/Source/Showcase/EmptyReflection.exr";var ci=(TextureImporter)AssetImporter.GetAtPath(reflection);ci.sRGBTexture=false;ci.textureShape=TextureImporterShape.TextureCube;ci.generateCubemap=TextureImporterGenerateCubemap.AutoCubemap;ci.maxTextureSize=64;ci.mipmapEnabled=true;var cp=ci.GetPlatformTextureSettings("Standalone");cp.overridden=true;cp.maxTextureSize=64;cp.format=TextureImporterFormat.BC6H;ci.SetPlatformTextureSettings(cp);ci.SaveAndReimport();material.SetTexture("_ReflectionCube",AssetDatabase.LoadAssetAtPath<Cubemap>(reflection));
  material.SetFloat("_RadianceGain",1);material.SetFloat("_DisplayContrast",1);material.SetFloat("_Roughness",name.StartsWith("Organic")?.48f:.6f);material.SetFloat("_Reflection",.35f);return material;
 }
 static void Showcase(Transform root){
  foreach(string name in new[]{"CabinetBaked","PinlightsBaked","OrganicBaked"}){
   string path=Base+"/Source/Showcase/"+name+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.isReadable=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importCameras=false;importer.importLights=false;importer.SaveAndReimport();
   var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));instance.transform.rotation=Quaternion.Euler(0,180,0)*instance.transform.rotation;
   if(name=="OrganicBaked")instance.transform.position=new Vector3(0,-.08645625f,.095f);
   foreach(var filter in instance.GetComponentsInChildren<MeshFilter>()){
    var material=ShowcaseSurface(name=="OrganicBaked"?"Organic_Sculpture__Flareworks_Studio":filter.name);
    for(int i=0;i<filter.sharedMesh.subMeshCount;i++){
     var renderer=Mesh(root,filter.sharedMesh,filter.transform.localToWorldMatrix,material,filter.name,i);
     if(filter.name=="Cabinet_Base"){
      // The source base has a 0.8 mm rounded front lip. Extend its existing
      // horizontal top to z=0; keep its height and the whole model scale.
      var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;
      for(int k=0;k<vertices.Length;k++)if(Mathf.Abs(vertices[k].y+.15f)<.00001f&&vertices[k].z<.001f)vertices[k].z=0;
      mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
     }
    }
   }
   UnityEngine.Object.DestroyImmediate(instance);
  }
  ExtendFloor(root,.6f,new Color(.025f,.025f,.028f),-.15f);
 }
}

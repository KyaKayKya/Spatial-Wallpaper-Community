using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
public static class BakedStudyBuilder {
 const string Root="Assets/Showcase/BakedStudy/";
 static Material Surface(string objectName,string variant){
  string path=Root+objectName+"_"+variant+".mat";
  var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!m){m=new Material(Shader.Find("SpatialWallpaper/BakedDisplaySurface"));AssetDatabase.CreateAsset(m,path);}
  m.SetTexture("_Radiance",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+objectName+"_"+variant+".exr"));
  m.SetTexture("_AgXLut",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"AcceptedAgX.png"));m.SetFloat("_RadianceGain",1);m.SetFloat("_DisplayContrast",1);
  m.SetFloat("_Roughness",objectName.StartsWith("Organic")?.48f:.6f);m.SetFloat("_Reflection",.35f);
  EditorUtility.SetDirty(m);return m;
 }
 public static string Install(){
  var li=(TextureImporter)AssetImporter.GetAtPath(Root+"AcceptedAgX.png");li.sRGBTexture=false;li.mipmapEnabled=false;li.textureCompression=TextureImporterCompression.Uncompressed;li.maxTextureSize=4096;li.wrapMode=TextureWrapMode.Clamp;li.SaveAndReimport();
  foreach(var file in Directory.GetFiles(Root,"*.exr")){
   var imp=(TextureImporter)AssetImporter.GetAtPath(file.Replace('\\','/'));
   int size=file.Contains("EmptyReflection")?128:file.Contains("CeilingPinlight")?256:(file.Contains("Back")||file.Contains("Organic"))?1024:512;
   imp.sRGBTexture=false;imp.mipmapEnabled=true;imp.isReadable=false;imp.maxTextureSize=size;
   var platform=imp.GetPlatformTextureSettings("Standalone");platform.overridden=true;platform.format=TextureImporterFormat.BC6H;
   platform.maxTextureSize=size;imp.SetPlatformTextureSettings(platform);imp.SaveAndReimport();
  }
  foreach(var name in new[]{"CabinetBaked.fbx","OrganicBaked.fbx","PinlightsBaked.fbx"}){
   var imp=(ModelImporter)AssetImporter.GetAtPath(Root+name);
   imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.generateSecondaryUV=false;imp.isReadable=false;imp.importCameras=false;imp.importLights=false;imp.SaveAndReimport();
  }
  var old=GameObject.Find("FixedCabinet");if(old)Object.DestroyImmediate(old);
  var slot=GameObject.Find("ReplaceableSlot");
  for(int i=slot.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(slot.transform.GetChild(i).gameObject);
  var frame=GameObject.Find("DisplayWindow_XY_Z0").transform;
  var cabinet=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"CabinetBaked.fbx"));
  cabinet.name="FixedCabinet";cabinet.transform.SetParent(frame,false);cabinet.transform.localRotation=Quaternion.Euler(0,180,0);cabinet.transform.localScale=new Vector3(1,.99375f,1);
  var oldFixtures=GameObject.Find("FixedPinlights");if(oldFixtures)Object.DestroyImmediate(oldFixtures);
  var fixtures=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"PinlightsBaked.fbx"));
  fixtures.name="FixedPinlights";fixtures.transform.SetParent(frame,false);fixtures.transform.localRotation=Quaternion.Euler(0,180,0);fixtures.transform.localScale=new Vector3(1,.99375f,1);
  var rs=cabinet.GetComponentsInChildren<MeshRenderer>().Concat(fixtures.GetComponentsInChildren<MeshRenderer>()).OrderBy(r=>r.name).ToArray();
  var receiver=cabinet.AddComponent<CabinetLightingReceivers>();receiver.surfaces=rs;receiver.emptyMaterials=new Material[rs.Length];
  var occupied=new Material[rs.Length];
  for(int i=0;i<rs.Length;i++){
   receiver.emptyMaterials[i]=Surface(rs[i].name,"empty");occupied[i]=Surface(rs[i].name,"occupied");rs[i].sharedMaterials=Enumerable.Repeat(occupied[i],rs[i].sharedMaterials.Length).ToArray();
   rs[i].shadowCastingMode=ShadowCastingMode.Off;rs[i].lightProbeUsage=LightProbeUsage.Off;rs[i].reflectionProbeUsage=ReflectionProbeUsage.Simple;
   GameObjectUtility.SetStaticEditorFlags(rs[i].gameObject,StaticEditorFlags.ReflectionProbeStatic);
  }
  var prop=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"OrganicBaked.fbx"));
  prop.name="OrganicSculpture";prop.transform.SetParent(slot.transform,false);prop.transform.localRotation=Quaternion.Euler(0,180,0)*prop.transform.localRotation;
  foreach(var r in prop.GetComponentsInChildren<MeshRenderer>()){
   r.sharedMaterial=Surface("Organic_Sculpture__Flareworks_Studio","occupied");r.shadowCastingMode=ShadowCastingMode.Off;r.lightProbeUsage=LightProbeUsage.Off;
  }
  var variant=slot.GetComponent<ReplaceableLightingVariant>();if(!variant)variant=slot.AddComponent<ReplaceableLightingVariant>();
  variant.occupied=occupied;variant.RecordBakedPlacement(prop.GetComponentInChildren<MeshRenderer>(),receiver);variant.cabinet=null;PrefabUtility.SaveAsPrefabAsset(slot,"Assets/Showcase/Prefabs/ReplaceableOrganic.prefab");
  variant.cabinet=receiver;receiver.cadence=Object.FindObjectOfType<AdaptiveRenderCadence>();receiver.UseVariant(variant,occupied);
  var rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Showcase/Settings/ShowcaseForward.asset");foreach(var feature in rd.rendererFeatures)feature.SetActive(false);rd.SetDirty();EditorUtility.SetDirty(rd);
  var urp=(UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
  var so=new SerializedObject(urp);so.FindProperty("m_MainLightShadowsSupported").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(urp);
  foreach(var light in Object.FindObjectsOfType<Light>()){light.shadows=LightShadows.None;light.intensity=0;}
  Lightmapping.lightingDataAsset=null;LightmapSettings.lightmaps=new LightmapData[0];
  var probes=Object.FindObjectOfType<LightProbeGroup>();if(probes)Object.DestroyImmediate(probes.gameObject);
  var profile=Object.FindObjectOfType<Volume>().sharedProfile;ColorAdjustments ca;profile.TryGet(out ca);ca.postExposure.Override(-.25f);EditorUtility.SetDirty(ca);
  Camera.main.allowHDR=false;Camera.main.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;urp.supportsHDR=false;EditorUtility.SetDirty(urp);Object.FindObjectOfType<PreviewEyeDriver>().exhibit=slot;
  ConfigureRig();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();return "Installed Cycles diffuse variants, view-dependent reflection, no realtime shadow or SSAO pass";
 }
 public static void ConfigureRig(){
  var frame=GameObject.Find("DisplayWindow_XY_Z0");var model=frame.transform.Find("SceneModel");if(!model){model=new GameObject("SceneModel").transform;model.SetParent(frame.transform,false);}
  GameObject.Find("FixedCabinet").transform.SetParent(model,true);GameObject.Find("FixedPinlights").transform.SetParent(model,true);GameObject.Find("InteriorContent_A").transform.SetParent(model,true);Object.FindObjectOfType<ReflectionProbe>().transform.SetParent(model,true);
  var rig=frame.GetComponent<PhysicalDisplayRig>();if(!rig)rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=Object.FindObjectOfType<WindowProjection>();rig.sceneModel=model;
 }
 public static string Reflection(){
  var slot=GameObject.Find("ReplaceableSlot");slot.SetActive(false);
  var receiver=Object.FindObjectOfType<CabinetLightingReceivers>();
  for(int i=0;i<receiver.surfaces.Length;i++)receiver.surfaces[i].sharedMaterial=receiver.emptyMaterials[i];
  var probe=Object.FindObjectOfType<ReflectionProbe>();
  string path=Root+"EmptyReflection.exr";bool ok;Shader.SetGlobalFloat("_RadianceCapture",1);try{ok=Lightmapping.BakeReflectionProbe(probe,path);}finally{Shader.SetGlobalFloat("_RadianceCapture",0);}
  if(ok){AssetDatabase.ImportAsset(path);probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Texture>(path);}
  slot.SetActive(true);var variant=slot.GetComponent<ReplaceableLightingVariant>();receiver.UseVariant(variant,variant.occupied);
  EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();return "Empty reflection baked="+ok;
 }
}

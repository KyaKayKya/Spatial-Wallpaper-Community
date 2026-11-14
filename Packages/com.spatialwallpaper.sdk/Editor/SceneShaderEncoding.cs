using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SpatialWallpaper.Content;
using UnityEditor;
using UnityEngine;

namespace SpatialWallpaper.Sdk.Editor {
 // Shared by legacy scene export and scripted snapshots. Only Shader assets
 // are built, inside the original fixed platform pipeline scope. The managed
 // gate validates the bundle before it can enter a package or native loader.
 internal static class SceneShaderEncoding {
  internal static byte[] Build(string scratch,IEnumerable<string> paths,IEnumerable<Material> consumers,HashSet<string> instanced,PresentationProfile profile){
   var inputs=paths.OrderBy(path=>path,StringComparer.Ordinal).ToArray();
   // Unity derives the internal serialized-file identity from the bundle name.
   // Distinct shader bundles in the same scene must not all share that identity.
   string bundleName="shader-"+PackageValidator.Digest(Encoding.UTF8.GetBytes(string.Join("\n",inputs)+"\n"+profile)).Substring(0,32)+".shaderbundle";
   var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);var stripping=settings.FindProperty("m_InstancingStripping");
   if(stripping==null||stripping.enumNames.Length!=3||stripping.enumNames[2]!="Keep All")throw new InvalidDataException("unsupported-instancing-settings");
   int previous=stripping.intValue;AssetBundleManifest manifest;
   var previousScope=ExportShaderVariants.InstancedSources;var previousConsumers=ExportShaderVariants.LocalConsumers;
   try{using(PlatformPipelineProfile.ShaderBuildScope(profile)){
    ExportShaderVariants.LocalConsumers=ExportShaderVariants.Consumers(consumers);ExportShaderVariants.InstancedSources=instanced;stripping.intValue=2;settings.ApplyModifiedPropertiesWithoutUndo();
    manifest=BuildPipeline.BuildAssetBundles(scratch,new[]{new AssetBundleBuild{assetBundleName=bundleName,assetNames=inputs}},BuildAssetBundleOptions.UncompressedAssetBundle,BuildTarget.StandaloneWindows64);
   }}finally{ExportShaderVariants.LocalConsumers=previousConsumers;ExportShaderVariants.InstancedSources=previousScope;stripping.intValue=previous;settings.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(settings.targetObject);}
   if(!manifest)throw new InvalidDataException("shader-build-failed");var bytes=File.ReadAllBytes(Path.Combine(scratch,bundleName));ShaderBundleGate.Validate(bytes);return bytes;
  }
 }
}

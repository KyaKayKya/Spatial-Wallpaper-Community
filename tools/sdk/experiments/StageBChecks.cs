using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;
using SpatialWallpaper.Sdk;
public static class StageBChecks {
 public static void Run(){
  string root=Environment.GetEnvironmentVariable("SDK_B_OUTPUT"),run=Environment.GetEnvironmentVariable("SDK_B_RUN");
  UnityEditor.PlayerSettings.colorSpace=UnityEngine.ColorSpace.Linear;
  UnityEditor.PlayerSettings.SetUseDefaultGraphicsAPIs(UnityEditor.BuildTarget.StandaloneWindows64,false);
  UnityEditor.PlayerSettings.SetGraphicsAPIs(UnityEditor.BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
  var bundles=Path.Combine(root,"bundles");Directory.CreateDirectory(bundles);
  var result=UnityEditor.BuildPipeline.BuildAssetBundles(bundles,new[]{new UnityEditor.AssetBundleBuild{assetBundleName="probe.shaderbundle",assetNames=new[]{"Assets/ContractProbe.shader"}}},UnityEditor.BuildAssetBundleOptions.UncompressedAssetBundle,UnityEditor.BuildTarget.StandaloneWindows64);
  if(!result)throw new Exception("shader-fixture-build-failed");
  ShaderBundleGate.Validate(File.ReadAllBytes(Path.Combine(bundles,"probe.shaderbundle")));
  var build=Admission.Admit(File.ReadAllBytes(Path.Combine(root,"positive.spatial")),"windows-x86_64");
  bool rejected=false;
  try{Admission.Admit(File.ReadAllBytes(Path.Combine(root,"malicious.spatial")),"windows-x86_64");}catch(InvalidDataException){rejected=true;}
  if(!rejected)throw new Exception("malicious-content-admitted");
  var builtin=new JObject();
  foreach(string name in new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Unlit"}){
   var shader=UnityEngine.Shader.Find(name);if(!shader)throw new Exception("missing-builtin-shader");
   var properties=new JObject();
   for(int i=0;i<shader.GetPropertyCount();i++)properties[shader.GetPropertyName(i)]=new JObject{{"type",(int)shader.GetPropertyType(i)},{"dimension",shader.GetPropertyType(i)==UnityEngine.Rendering.ShaderPropertyType.Texture?(int)shader.GetPropertyTextureDimension(i):0}};
   builtin[name]=properties;
  }
  File.WriteAllText(Path.Combine(root,"builtin-shaders.json"),builtin.ToString());
  File.WriteAllText(Path.Combine(root,"unity-result.json"),new JObject{{"run_id",run},{"accepted",build.BuildId},{"rejected",rejected},{"engine",UnityEngine.Application.unityVersion}}.ToString());
 }
}

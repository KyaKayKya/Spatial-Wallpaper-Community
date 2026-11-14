"""Build an external shader-only fixture before freezing native resource admission."""
from pathlib import Path
import os
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Validation/Content/resource-probe'
PROJECT = OUT/'author'
UNITY = Path('D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe')

def build():
    for name in ['Assets/Editor','Assets/Input','Packages','ProjectSettings']:
        (PROJECT/name).mkdir(parents=True,exist_ok=True)
    for name in ['manifest','packages-lock']:
        shutil.copy2(ROOT/('tools/content/probe-'+name+'.json'), PROJECT/('Packages/'+name+'.json'))
    shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt', PROJECT/'ProjectSettings/ProjectVersion.txt')
    for relative in ['Train/Shaders/WinterJourney.shader','Pool/Shaders/PoolReflection.shader','Pool/Shaders/PoolWater.shader','Pool/Shaders/PoolCeramic.shader','Parking/Shaders/Lamp.shader','Parking/Shaders/CRT.shader','Parking/Shaders/Asphalt.shader','Showcase/Shaders/BakedDisplaySurface.shader']:
        shutil.copy2(ROOT/'Prototype/Assets'/relative, PROJECT/'Assets/Input'/Path(relative).name)
    (PROJECT/'Assets/Editor/ResourceBuild.cs').write_text('''
using UnityEditor;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Linq;
public static class ResourceBuild {
 public static void Run(){
  Directory.CreateDirectory("Assets/Generated");
  var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/Generated/Renderer.asset");
  var pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/Generated/Pipeline.asset");UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
  string target=Environment.GetEnvironmentVariable("RESOURCE_PROBE_OUTPUT");
  Directory.CreateDirectory(target);
  var builds=new[]{new AssetBundleBuild{assetBundleName="shaders",assetNames=AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Input"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray()}};
  if(!BuildPipeline.BuildAssetBundles(target,builds,BuildAssetBundleOptions.UncompressedAssetBundle|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64))throw new Exception("shader bundle failed");
 }
}
''',encoding='utf-8')
    env=os.environ.copy()
    env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
    env['RESOURCE_PROBE_OUTPUT']=str(OUT/'bundles')
    result=subprocess.run([str(UNITY),'-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','ResourceBuild.Run','-logFile',str(OUT/'build.log')],env=env,timeout=1200)
    print('shader-only fixture exit',result.returncode,flush=True)
    raise SystemExit(result.returncode)

if __name__=='__main__': build()

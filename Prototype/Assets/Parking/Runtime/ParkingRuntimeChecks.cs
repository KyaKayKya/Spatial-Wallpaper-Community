using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Parking {
 public sealed class ParkingRuntimeChecks:MonoBehaviour {
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--parking-checks")>=0)new GameObject("ParkingRuntimeChecks").AddComponent<ParkingRuntimeChecks>();}
  static void Require(bool condition,string error){if(!condition)throw new Exception(error);}
  IEnumerator Start(){
   yield return null;
   var content=FindObjectOfType<ParkingAtmosphere>();var projection=FindObjectOfType<WindowProjection>();
   content.externalClock=true;
   var reference=FindObjectOfType<ParkingContentReference>();GameObject instance=null;
   if(reference){instance=Instantiate(reference.prefab,content.transform.parent,false);yield return null;}
   string output=Application.persistentDataPath;var args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"--output");if(ix>=0&&ix+1<args.Length)output=args[ix+1];Directory.CreateDirectory(output);
   string result="";
   try{
    if(reference){
     var restored=instance.GetComponent<ParkingAtmosphere>();int index=restored.ground.lightmapIndex;
     Require(index>=0&&index<LightmapSettings.lightmaps.Length&&LightmapSettings.lightmaps[index].lightmapColor==reference.expectedGroundLightmap,"Instantiated content lost Bakery ground lighting");
     instance.SetActive(false);DestroyImmediate(instance);instance=null;
     Require(content.ground.lightmapIndex>=0&&LightmapSettings.lightmaps[content.ground.lightmapIndex].lightmapColor==reference.expectedGroundLightmap,"Removing replacement corrupted original lightmap");
    }
    var box=FindObjectOfType<DisplaySceneVolume>();Require(box&&!box.showGuide,"Missing fixed scene box or guide left visible");
    foreach(var mesh in content.GetComponentsInChildren<MeshFilter>(true))CheckBounds(box,mesh.sharedMesh.bounds,mesh.transform.localToWorldMatrix,mesh.name);
    var field=content.GetComponent<ParkingLampField>();
    Require(field.positions.Any(p=>Mathf.Abs(p.x)>12),"Open parking collapsed into a narrow box corridor");
    foreach(var p in field.positions)CheckBounds(box,field.mesh.bounds,field.transform.localToWorldMatrix*Matrix4x4.TRS(p,Quaternion.identity,Vector3.one*field.instanceScale),"Instanced lamp");
    Require(content.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="TelevisionStack")==1,"Repeated television group");
    Require(content.streetLight.shadows==LightShadows.Soft,"Near lamp lost real soft shadows");
    var previousEnvironment=RenderSettings.ambientProbe;
    var envA=new GameObject("EnvironmentA");envA.SetActive(false);envA.AddComponent<ParkingEnvironmentBinding>().irradiance=new Color(.01f,.02f,.03f);envA.SetActive(true);
    var envB=new GameObject("EnvironmentB");envB.SetActive(false);envB.AddComponent<ParkingEnvironmentBinding>().irradiance=new Color(.08f,.07f,.06f);envB.SetActive(true);var activeEnvironment=RenderSettings.ambientProbe;
    DestroyImmediate(envA);Require(RenderSettings.ambientProbe.Equals(activeEnvironment),"Old content overwrote new environment");
    DestroyImmediate(envB);Require(RenderSettings.ambientProbe.Equals(previousEnvironment),"Environment stack failed to restore host");
    foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,.1f,-.4f),new Vector3(-.2f,-.1f,-.9f)}){
     projection.SetEye(eye);
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      var corner=box.FrontCorner(x,y);var expected=projection.screenFrame.TransformPoint(new Vector3(x*projection.displayWidth,y*projection.displayHeight,0));
      Require(Vector3.Distance(corner,expected)<.000001f,"Actual box aperture does not match display");
      var q=projection.GetComponent<Camera>().WorldToViewportPoint(corner);
      Require(new Vector2(q.x-x-.5f,q.y-y-.5f).magnitude<.00001f,"Actual box corner moves under eye motion");
     }
     foreach(float x in new[]{-.5f,.5f}){
      var edge=projection.screenFrame.TransformPoint(new Vector3(x*projection.displayWidth,-projection.displayHeight*.5f,0));
      var groundLocal=content.transform.InverseTransformPoint(edge);
      Require(Mathf.Abs(groundLocal.y)<.0001f,"Ground does not meet physical screen lower edge");
      Require(Mathf.Abs(projection.GetComponent<Camera>().WorldToViewportPoint(edge).y)<.00001f,"Ground edge leaves viewport bottom under eye motion");
      var groundMesh=content.ground.GetComponent<MeshFilter>();
      Require(groundMesh.sharedMesh.vertices.Any(v=>Vector3.Distance(groundMesh.transform.TransformPoint(v),edge)<.000001f),"Floor mesh has no actual aperture corner vertex");
     }
    }
    float min=10,max=0;
    for(int i=0;i<360;i++){
     float t=i/30f;content.Evaluate(t);float signal=ParkingAtmosphere.ScreenSignal(t,0);min=Mathf.Min(min,signal);max=Mathf.Max(max,signal);
     Require(Mathf.Abs(content.screens[0].sharedMaterial.GetFloat("_Brightness")-content.ground.sharedMaterial.GetVector("_TV0").w)<.00001f,"Screen / reflection out of sync");
     Require(signal==ParkingAtmosphere.ScreenSignal(t,0),"Effect is not deterministic");
    }
    Require(max-min>.4f,"Insufficient TV brightness variation");
    Require(ParkingAtmosphere.LampSignal(10.1f)<.6f&&ParkingAtmosphere.LampSignal(0)>.8f,"Streetlight fault not observable");
    content.SetTelevisionsVisible(false);content.Evaluate(2);
    Require(!content.TelevisionsVisible&&!content.screenLights[0].isActiveAndEnabled&&content.streetLight.isActiveAndEnabled,"TV toggle affected streetlight or failed to hide TVs");
    Require(content.ground.sharedMaterial.GetVector("_TV0").w==0&&content.ground.sharedMaterial.GetVector("_TV1").w==0&&content.ground.sharedMaterial.GetFloat("_TVVisible")==0,"Hidden TVs leave light/reflection/contact ghosts");
    content.SetTelevisionsVisible(true);Require(content.TelevisionsVisible&&content.ground.sharedMaterial.GetVector("_TV0").w>0,"TV restore failed");
    content.gameObject.SetActive(false);
    Require(!content.GetComponentsInChildren<Light>(true).Any(l=>l.isActiveAndEnabled),"Content disabled but child lights remain active");
    Require(projection.isActiveAndEnabled&&projection.SetEye(new Vector3(.1f,0,-.6f)),"Content disable removed projection");
    content.gameObject.SetActive(true);content.Evaluate(0);
    Require(content.screenLights[0].isActiveAndEnabled,"Content restore lost screen light");
    var pipeline=projection.GetComponent<ParkingPipelineBinding>();pipeline.enabled=false;
    Require(QualitySettings.renderPipeline==pipeline.restoreOverride,"Scene pipeline not restored");
    foreach(var prior in new RenderPipelineAsset[]{null,GraphicsSettings.defaultRenderPipeline,pipeline.pipeline}){
     QualitySettings.renderPipeline=prior;var test=new GameObject("PipelineLifecycleTest");test.SetActive(false);var generic=test.AddComponent<ParkingPipelineBinding>();generic.pipeline=pipeline.pipeline;
     test.SetActive(true);Require(QualitySettings.renderPipeline==generic.pipeline,"Generic pipeline not applied");test.SetActive(false);Require(QualitySettings.renderPipeline==prior,"Generic binding changed original quality pipeline");Destroy(test);
    }
    QualitySettings.renderPipeline=pipeline.restoreOverride;pipeline.enabled=true;
    Require(QualitySettings.renderPipeline==pipeline.pipeline,"Scene pipeline not applied");
    var floor=content.ground;var clonedMaterial=floor.sharedMaterial;DestroyImmediate(content);
    Require(floor.sharedMaterial&&floor.sharedMaterial!=clonedMaterial,"Effect removal did not restore original renderer material");
    result="PASS: actual reference aperture corners and floor mesh vertices fixed across eye positions; open field extends laterally beyond reference box; all geometry behind aperture and above floor; unique TV group; guide hidden; "+(reference?"Bakery prefab instantiate/remove preserves ground lighting; ":"")+"real near soft shadows; overlapping environment ownership restores host; 360 deterministic light/reflection samples; independent TV hide/restore clears spill/reflection/contact; content disable/restore; projection survives content removal; generic pipeline restores null/other/same; standalone override; effect removal restores renderer materials.\n";
   }catch(Exception e){result="FAIL: "+e;Debug.LogException(e);}
   File.WriteAllText(Path.Combine(output,"runtime-checks.txt"),result);Application.Quit(result.StartsWith("PASS")?0:1);
  }
  static void CheckBounds(DisplaySceneVolume box,Bounds b,Matrix4x4 matrix,string name){
   foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
   {
    var p=box.transform.InverseTransformPoint(matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
    Require(p.z>=-.00001f&&p.z<=box.size.z+.00001f&&p.y>=-box.size.y*.5f-.00001f,name+" crosses the aperture, floor or far boundary");
   }
  }
 }
}

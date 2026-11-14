using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpatialWallpaper.Train;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Editor;
public static class TrainContentExport {
 public static void Run(){
  PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  EditorSceneManager.OpenScene("Assets/Library/Scenes/train.unity");
  var root=GameObject.Find("SceneModel").transform;var camera=UnityEngine.Object.FindObjectsOfType<Camera>().Single(c=>c.name=="DisplayCamera");
  var volume=root.GetComponent<DisplaySceneVolume>();if(!volume)throw new InvalidOperationException("train-frame-missing");
  var definition=new GameObject("SDK Author").AddComponent<SceneDefinition>();definition.contentRoot=root;definition.templateId="1278607b-5d1d-4b48-b156-0e53301ddf8b";definition.displayName="Train";definition.authorFrame=Vector3.Scale(volume.size,volume.transform.lossyScale);
  definition.licenseSource="Original local Train assets; local validation only";
  // Replace only the audited author helpers. Unsupported components still
  // reach the normal SDK exporter and are rejected there.
  var journey=root.GetComponentsInChildren<TrainJourney>().Single();
  var bindings=journey.gameObject.AddComponent<SignalBindings>();var materials=new System.Collections.Generic.List<MaterialSignalBinding>();
  foreach(var renderer in journey.windows){
   if(!renderer)throw new InvalidOperationException("train-window-missing");
   var owned=renderer.sharedMaterials.Select(m=>new Material(m)).ToArray();renderer.sharedMaterials=owned;
   foreach(var material in owned){if(!material.HasProperty("_JourneyTime"))throw new InvalidOperationException("train-window-clock-missing");materials.Add(new MaterialSignalBinding{material=material,property="_JourneyTime",kind=MaterialSignalKind.Time});}
  }
  bindings.materials=materials.ToArray();UnityEngine.Object.DestroyImmediate(journey);UnityEngine.Object.DestroyImmediate(volume);
  var presentation=root.gameObject.AddComponent<ScenePresentationSettings>();presentation.customBackground=true;presentation.backgroundColor=camera.backgroundColor;presentation.customClipDepth=true;presentation.clipDepth=camera.GetComponent<WindowProjection>().contentDepth;
  presentation.customCamera=true;presentation.profile=PresentationProfile.Forward;presentation.hdr=camera.allowHDR;presentation.msaa=camera.allowMSAA?((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).msaaSampleCount:1;var cameraData=camera.GetUniversalAdditionalCameraData();presentation.opaqueTexture=cameraData.requiresColorTexture;presentation.depthTexture=cameraData.requiresDepthTexture;presentation.cameraPostProcessing=cameraData.renderPostProcessing;
  string output=Environment.GetEnvironmentVariable("CONTENT_BUILD_OUTPUT");
  File.WriteAllText(Path.Combine(output,"reports/train-source-meshes.json"),new Newtonsoft.Json.Linq.JArray(root.GetComponentsInChildren<MeshFilter>().Select(f=>(Newtonsoft.Json.Linq.JToken)new Newtonsoft.Json.Linq.JObject{["node"]=f.name,["asset"]=AssetDatabase.GetAssetPath(f.sharedMesh),["vertices"]=f.sharedMesh.vertexCount,["submeshes"]=f.sharedMesh.subMeshCount})).ToString());
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh.vertexCount>400000)SplitMesh(filter);
  try{var id=ScenePackageExporter.Export(definition,Path.Combine(output,"Train.spatial"),ExportMode.LocalValidation);File.WriteAllText(Path.Combine(output,"train-build-id.txt"),id);}
  catch(Exception error){File.WriteAllText(Path.Combine(output,"reports/train-export-failure.json"),new Newtonsoft.Json.Linq.JObject{["passed"]=false,["error"]=error.Message}.ToString());throw;}
 }
 // Train's Details FBX exceeds the per-resource limit. Partition triangles
 // without changing any vertex attribute; normal resource limits still apply.
 static void SplitMesh(MeshFilter filter){
  var source=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();if(source.subMeshCount!=1||source.GetTopology(0)!=MeshTopology.Triangles||renderer.sharedMaterials.Length!=1)throw new InvalidOperationException("train-split-layout");
  var p=source.vertices;var n=source.normals;var t=source.tangents;var uv=source.uv;var uv2=source.uv2;var c=source.colors;var triangles=source.GetTriangles(0);
  var map=new System.Collections.Generic.Dictionary<int,int>();var originals=new System.Collections.Generic.List<int>();var indices=new System.Collections.Generic.List<int>();int parts=0,total=0;
  Action flush=()=>{
   if(indices.Count==0)return;var mesh=new Mesh{name=source.name+" part "+parts,indexFormat=IndexFormat.UInt32};
   mesh.vertices=originals.Select(i=>p[i]).ToArray();if(n.Length==p.Length)mesh.normals=originals.Select(i=>n[i]).ToArray();if(t.Length==p.Length)mesh.tangents=originals.Select(i=>t[i]).ToArray();if(uv.Length==p.Length)mesh.uv=originals.Select(i=>uv[i]).ToArray();if(uv2.Length==p.Length)mesh.uv2=originals.Select(i=>uv2[i]).ToArray();if(c.Length==p.Length)mesh.colors=originals.Select(i=>c[i]).ToArray();mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
   var actualP=mesh.vertices;var actualN=mesh.normals;var actualT=mesh.tangents;var actualUv=mesh.uv;var actualUv2=mesh.uv2;var actualC=mesh.colors;
   for(int j=0;j<indices.Count;j++){int a=indices[j],b=triangles[total+j];if(!actualP[a].Equals(p[b])||n.Length==p.Length&&!actualN[a].Equals(n[b])||t.Length==p.Length&&!actualT[a].Equals(t[b])||uv.Length==p.Length&&!actualUv[a].Equals(uv[b])||uv2.Length==p.Length&&!actualUv2[a].Equals(uv2[b])||c.Length==p.Length&&!actualC[a].Equals(c[b]))throw new InvalidOperationException("train-split-attribute-changed");}
   var child=new GameObject(source.name+" part "+parts++);child.transform.SetParent(filter.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;var target=child.AddComponent<MeshRenderer>();target.sharedMaterials=renderer.sharedMaterials;target.shadowCastingMode=renderer.shadowCastingMode;target.receiveShadows=renderer.receiveShadows;target.lightmapIndex=renderer.lightmapIndex;target.lightmapScaleOffset=renderer.lightmapScaleOffset;target.lightProbeUsage=renderer.lightProbeUsage;target.reflectionProbeUsage=renderer.reflectionProbeUsage;total+=indices.Count;map.Clear();originals.Clear();indices.Clear();
  };
  for(int j=0;j<triangles.Length;j+=3){if(map.Count+3>400000)flush();for(int k=0;k<3;k++){int original=triangles[j+k];if(!map.TryGetValue(original,out int index)){index=map.Count;map.Add(original,index);originals.Add(original);}indices.Add(index);}}
  flush();if(total!=triangles.Length)throw new InvalidOperationException("train-split-triangle-loss");UnityEngine.Object.DestroyImmediate(renderer);UnityEngine.Object.DestroyImmediate(filter);
 }
}

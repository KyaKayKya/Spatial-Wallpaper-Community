using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialWallpaper.Sdk.Editor {
 public enum ExportMode { LocalValidation, Share }
 public static class ScenePackageExporter {
  public static string Export(SceneDefinition definition,string outputPath,ExportMode mode){
   if(!definition||!definition.contentRoot)throw new InvalidDataException("missing-scene-definition");
   // Public distribution needs a reviewed dependency record, added by the
   // complete sharing workflow. Local validation never implies permission.
   if(mode!=ExportMode.LocalValidation)throw new InvalidDataException("share-license-review-required");
   if(Application.unityVersion!="2022.3.62f3")throw new InvalidDataException("unsupported-editor");
   if(QualitySettings.activeColorSpace!=ColorSpace.Linear)throw new InvalidDataException("linear-color-space-required");
   string target=Path.GetFullPath(outputPath);Directory.CreateDirectory(Path.GetDirectoryName(target));
   string temp=target+"."+Guid.NewGuid().ToString("N")+".partial";string scratch=Path.Combine("Library","SpatialExport",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
   try {
    var builder=new Builder(definition,scratch);var scene=builder.Build();SceneData.Validate(scene);
    var files=builder.Files;files.Add("scene.json",Encoding.UTF8.GetBytes(scene.ToString(Formatting.None)));
    var manifest=new JObject{
     ["schema"]=2,["template_id"]=definition.templateId,["name"]=definition.displayName,["build_id"]="",
     ["engine"]="2022.3.62f3",["pipeline"]="14.0.12",["sdk"]="0.2.0",["platform"]="windows-x86_64",["color_space"]="linear",
     ["capabilities"]=scene.ContainsKey("effects")?new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1"):new JArray("static-data-v1","shader-only-2022.3.62f3-v1"),["entry"]="scene.json",
     ["files"]=new JObject(files.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new JProperty(p.Key,new JObject{["size"]=p.Value.Length,["sha256"]=PackageValidator.Digest(p.Value)}))),
     ["license"]=new JObject{["mode"]="local-validation",["records"]=new JArray(new JObject{["name"]=definition.displayName,["source"]=definition.licenseSource,["license"]=definition.licenseName,["redistribution"]="unverified",["attribution"]=""})},
     ["editable"]=definition.editable,["reshare"]=definition.reshare
    };
    manifest["build_id"]=PackageValidator.Identity(manifest);files.Add("manifest.json",Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None)));
    using(var stream=File.Create(temp))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))foreach(var p in files.OrderBy(p=>p.Key,StringComparer.Ordinal))using(var dest=zip.CreateEntry(p.Key,System.IO.Compression.CompressionLevel.Optimal).Open())dest.Write(p.Value,0,p.Value.Length);
    PackageValidator.Validate(temp);
    if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
    return (string)manifest["build_id"];
   }finally{if(File.Exists(temp))File.Delete(temp);Directory.Delete(scratch,true);}
  }
  sealed class Builder {
   readonly SceneDefinition definition;readonly string scratch;
   public readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
   readonly Dictionary<Mesh,int> meshIds=new Dictionary<Mesh,int>();readonly Dictionary<Texture,int> textureIds=new Dictionary<Texture,int>();readonly Dictionary<Material,int> materialIds=new Dictionary<Material,int>();
   readonly Dictionary<string,int> meshContentIds=new Dictionary<string,int>(StringComparer.Ordinal),textureContentIds=new Dictionary<string,int>(StringComparer.Ordinal);
   readonly JArray meshes=new JArray(),textures=new JArray(),materials=new JArray();readonly Dictionary<Transform,int> nodes=new Dictionary<Transform,int>();
   readonly HashSet<string> shaders=new HashSet<string>(StringComparer.Ordinal);
   readonly HashSet<string> instancedShaders=new HashSet<string>(StringComparer.Ordinal);
   public Builder(SceneDefinition definition,string scratch){this.definition=definition;this.scratch=scratch;}
   public JObject Build(){
    var objects=definition.contentRoot.GetComponentsInChildren<Transform>(false);var data=new JArray();
    foreach(var t in objects){
     foreach(var component in t.GetComponents<Component>())if(!component||!(component is Transform||component is MeshFilter||component is MeshRenderer||component is InstanceField||component is SignalBindings||component is Light||component is ScenePresentationSettings||component is TransformOscillation||component is SurfaceMaterialBinding||component is PlanarReflection||component is ReflectionProbe))throw new InvalidDataException("unsupported-author-component:"+(component?component.GetType().Name:"missing-script"));
     var renderer=t.GetComponent<MeshRenderer>();var mesh=t.GetComponent<MeshFilter>();if((bool)renderer!=(bool)mesh)throw new InvalidDataException("mesh-renderer-pair-required");
     int parent=t==definition.contentRoot?-1:nodes[t.parent];nodes.Add(t,nodes.Count);
     data.Add(new JObject{["name"]=t.name,["parent"]=parent,["layer"]=t.gameObject.layer,["position"]=V(t.localPosition),["rotation"]=Q(t.localRotation),["scale"]=V(t.localScale),
      ["mesh"]=mesh?MeshId(mesh.sharedMesh):-1,["materials"]=renderer?new JArray(renderer.sharedMaterials.Select(MaterialId)):new JArray(),
      ["lightmap"]=renderer&&renderer.lightmapIndex<LightmapSettings.lightmaps.Length?renderer.lightmapIndex:-1,["lightmapST"]=renderer?V(renderer.lightmapScaleOffset):new JArray(1,1,0,0)});
    }
    var lightmaps=new JArray();foreach(var lm in LightmapSettings.lightmaps)lightmaps.Add(new JArray(TextureId(lm.lightmapColor),TextureId(lm.lightmapDir),TextureId(lm.shadowMask)));
    var effects=new JArray();foreach(var t in objects){
     var presentation=t.GetComponent<ScenePresentationSettings>();if(presentation){if(presentation.customAmbientGradient){if(presentation.customAmbient)throw new InvalidDataException("duplicate-author-ambient");effects.Add(new JObject{["kind"]="ambient-gradient",["node"]=nodes[t],["sky"]=C(presentation.ambientSky),["equator"]=C(presentation.ambientEquator),["ground"]=C(presentation.ambientGround)});}if(presentation.customAmbient)effects.Add(new JObject{["kind"]="ambient",["node"]=nodes[t],["irradiance"]=C(presentation.ambientIrradiance)});if(presentation.postProcessing)effects.Add(new JObject{["kind"]="post",["node"]=nodes[t],["bloom"]=V(presentation.bloom),["aces"]=presentation.aces,["exposure"]=presentation.exposure,["saturation"]=presentation.saturation,["vignette"]=new JArray(presentation.vignette.x,presentation.vignette.y)});}
     if(presentation&&presentation.customBackground)effects.Add(new JObject{["kind"]="background",["node"]=nodes[t],["color"]=C(presentation.backgroundColor)});
     if(presentation&&presentation.customClipDepth)effects.Add(new JObject{["kind"]="clip-depth",["node"]=nodes[t],["depth"]=presentation.clipDepth});
     if(presentation&&presentation.customCamera){if(!Enum.IsDefined(typeof(PresentationProfile),presentation.profile))throw new InvalidDataException("unsupported-author-camera-profile");effects.Add(new JObject{["kind"]="camera",["node"]=nodes[t],["hdr"]=presentation.hdr,["msaa"]=presentation.msaa,["opaque"]=presentation.opaqueTexture,["depth"]=presentation.depthTexture,["post"]=presentation.cameraPostProcessing,["renderer"]=presentation.profile==PresentationProfile.Forward?"forward":"forward-plus"});}
     var motion=t.GetComponent<TransformOscillation>();if(motion)effects.Add(new JObject{["kind"]="oscillation",["node"]=nodes[t],["position"]=Harmonics(motion.position),["rotation"]=Harmonics(motion.rotation)});
     var surface=t.GetComponent<SurfaceMaterialBinding>();if(surface)effects.Add(new JObject{["kind"]="surface-material",["node"]=nodes[t],["materials"]=new JArray(surface.materials.Select(MaterialId)),["depthPerScale"]=surface.depthPerScale,["tiling"]=V(surface.causticsTiling)});
     var planar=t.GetComponent<PlanarReflection>();if(planar)effects.Add(new JObject{["kind"]="planar-reflection",["node"]=nodes[t],["material"]=MaterialId(planar.material),["resolution"]=planar.resolutionScale,["excluded"]=new JArray(planar.excluded.Select(r=>nodes[r.transform]))});
     var probe=t.GetComponent<ReflectionProbe>();if(probe){if(!probe.enabled||probe.mode!=ReflectionProbeMode.Realtime||probe.refreshMode!=ReflectionProbeRefreshMode.OnAwake||probe.timeSlicingMode!=ReflectionProbeTimeSlicingMode.NoTimeSlicing||probe.resolution!=128||!probe.hdr||probe.cullingMask!=-1||probe.clearFlags!=ReflectionProbeClearFlags.SolidColor||probe.renderDynamicObjects)throw new InvalidDataException("unsupported-author-reflection-probe");effects.Add(new JObject{["kind"]="reflection-probe",["node"]=nodes[t],["resolution"]=128,["size"]=V(probe.size),["center"]=V(probe.center),["near"]=probe.nearClipPlane,["far"]=probe.farClipPlane,["shadowDistance"]=probe.shadowDistance,["background"]=C(probe.backgroundColor),["intensity"]=probe.intensity,["blend"]=probe.blendDistance,["box"]=probe.boxProjection,["importance"]=probe.importance});}
     var field=t.GetComponent<InstanceField>();if(field)effects.Add(new JObject{["kind"]="instances",["node"]=nodes[t],["mesh"]=MeshId(field.mesh),["materials"]=new JArray(field.materials.Select(MaterialId)),["positions"]=new JArray(field.positions.Select(p=>(JToken)V(p))),["scale"]=field.instanceScale});
     var light=t.GetComponent<Light>();if(light&&!FullyBaked(light)){if(light.cookie||light.flare||light.type!=LightType.Point&&light.type!=LightType.Spot&&light.type!=LightType.Directional)throw new InvalidDataException("unsupported-author-light");effects.Add(new JObject{["kind"]="light",["node"]=nodes[t],["mask"]=light.cullingMask,["type"]=light.type==LightType.Point?"point":light.type==LightType.Spot?"spot":"directional",["color"]=C(light.color),["intensity"]=light.intensity,["range"]=light.range,["spot"]=light.spotAngle,["innerSpot"]=light.innerSpotAngle,["shadows"]=(int)light.shadows,["bias"]=light.shadowBias,["normalBias"]=light.shadowNormalBias,["near"]=light.shadowNearPlane});}
     foreach(var signal in t.GetComponents<SignalBindings>()){if(!signal.enabled||!Enum.IsDefined(typeof(SignalKind),signal.kind)||signal.materials.Any(b=>!Enum.IsDefined(typeof(MaterialSignalKind),b.kind)))throw new InvalidDataException("unsupported-author-signal");if(signal.lights.Any(b=>b.light&&FullyBaked(b.light)))throw new InvalidDataException("baked-light-signal-unsupported");effects.Add(new JObject{["kind"]="signal",["node"]=nodes[t],["signal"]=signal.kind==SignalKind.Flicker?"flicker":"intermittent",["seed"]=signal.seed,["materials"]=new JArray(signal.materials.Select(b=>new JObject{["material"]=MaterialId(b.material),["property"]=b.property,["type"]=b.kind==MaterialSignalKind.VectorW?"vector-w":b.kind==MaterialSignalKind.Color?"color":b.kind==MaterialSignalKind.Time?"time":"float",["basis"]=V(b.basis),["gain"]=b.gain})),["lights"]=new JArray(signal.lights.Select(b=>new JObject{["node"]=nodes[b.light.transform],["intensity"]=b.intensity}))});}
    }
    if(shaders.Count>0){
     // Runtime-created instance fields are absent from Unity's build-time
     // material dependency scan. Retain their Shader variants explicitly.
     var cameraEffect=effects.OfType<JObject>().SingleOrDefault(e=>(string)e["kind"]=="camera");var profile=cameraEffect!=null&&(string)cameraEffect["renderer"]=="forward"?PresentationProfile.Forward:PresentationProfile.ForwardPlus;
     var bytes=SceneShaderEncoding.Build(scratch,shaders,materialIds.Keys.Where(m=>shaders.Contains(AssetDatabase.GetAssetPath(m.shader))),instancedShaders,profile);Files.Add("custom.shaderbundle",bytes);
    }
    if(LightmapSettings.lightmapsMode!=LightmapsMode.NonDirectional&&LightmapSettings.lightmapsMode!=LightmapsMode.CombinedDirectional)throw new InvalidDataException("unsupported-lightmap-mode");
    var result=new JObject{["frame"]=V(definition.authorFrame),["nodes"]=data,["meshes"]=meshes,["textures"]=textures,["materials"]=materials,["lightmaps"]=lightmaps,["lightmapMode"]=LightmapSettings.lightmapsMode==LightmapsMode.NonDirectional?"non-directional":"directional",
     ["environment"]=new JObject{["ambient"]=C(RenderSettings.ambientLight),["fog"]=RenderSettings.fog,["fogColor"]=C(RenderSettings.fogColor),["fogDensity"]=RenderSettings.fogDensity}};
    if(effects.Count>0)result["effects"]=effects;return result;
   }
   static bool FullyBaked(Light light)=>light.bakingOutput.isBaked&&light.bakingOutput.lightmapBakeType==LightmapBakeType.Baked;
   static JArray Harmonics(TransformHarmonic[] harmonics)=>new JArray(harmonics.Select(h=>(JToken)new JObject{["amplitude"]=V(h.amplitude),["frequency"]=V(h.frequency),["phase"]=V(h.phase)}));
   int MeshId(Mesh mesh){
    if(!mesh)throw new InvalidDataException("missing-mesh");if(meshIds.TryGetValue(mesh,out int id))return id;
    var bytes=SceneResourceEncoding.Mesh(mesh);string name="meshes/"+PackageValidator.Digest(bytes)+".smesh";if(!meshContentIds.TryGetValue(name,out id)){id=meshes.Count;meshes.Add(name);Files.Add(name,bytes);meshContentIds.Add(name,id);}meshIds.Add(mesh,id);return id;
   }
   int TextureId(Texture texture){
    if(!texture)return -1;if(textureIds.TryGetValue(texture,out int id))return id;if(!(texture is Texture2D)&&!(texture is Cubemap))throw new InvalidDataException("only-static-texture2d-or-cubemap");var bytes=SceneResourceEncoding.Texture(texture);string name="textures/"+PackageValidator.Digest(bytes)+".stex";if(!textureContentIds.TryGetValue(name,out id)){id=textures.Count;textures.Add(name);Files.Add(name,bytes);textureContentIds.Add(name,id);}textureIds.Add(texture,id);return id;
   }
   int MaterialId(Material material){
    if(!material)throw new InvalidDataException("missing-material");if(materialIds.TryGetValue(material,out int id))return id;var shader=material.shader;
    string bundle="";if(shader.name!="Universal Render Pipeline/Lit"&&shader.name!="Universal Render Pipeline/Unlit"){
     string source=AssetDatabase.GetAssetPath(shader);if(!source.StartsWith("Assets/",StringComparison.Ordinal)&&!source.StartsWith("Packages/",StringComparison.Ordinal))throw new InvalidDataException("shader-has-no-source");shaders.Add(source);if(material.enableInstancing)instancedShaders.Add(source);bundle="custom.shaderbundle";
    }
    var properties=new JObject();for(int i=0;i<ShaderUtil.GetPropertyCount(shader);i++){
     string name=ShaderUtil.GetPropertyName(shader,i);var type=ShaderUtil.GetPropertyType(shader,i);JObject value;
     switch(type){
      case ShaderUtil.ShaderPropertyType.Color:value=new JObject{["type"]="color",["value"]=C(material.GetColor(name))};break;
      case ShaderUtil.ShaderPropertyType.Vector:value=new JObject{["type"]="vector",["value"]=V(material.GetVector(name))};break;
      case ShaderUtil.ShaderPropertyType.Float:case ShaderUtil.ShaderPropertyType.Range:value=new JObject{["type"]="float",["value"]=material.GetFloat(name)};break;
      case ShaderUtil.ShaderPropertyType.TexEnv:
       value=new JObject{["type"]="texture",["value"]=TextureId(material.GetTexture(name))};
       Vector2 offset=material.GetTextureOffset(name),scale=material.GetTextureScale(name);string st=name+"_ST";
       if(material.HasProperty(st))properties[st]=new JObject{["type"]="vector",["value"]=new JArray(scale.x,scale.y,offset.x,offset.y)};break;
      default:throw new InvalidDataException("unsupported-material-property");
     }properties[name]=value;
    }
    id=materials.Count;materials.Add(new JObject{["shader"]=shader.name,["bundle"]=bundle,["properties"]=properties,["keywords"]=new JArray(material.shaderKeywords),["queue"]=material.renderQueue,["instancing"]=material.enableInstancing});materialIds.Add(material,id);return id;
   }
   static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);static JArray V(Vector4 v)=>new JArray(v.x,v.y,v.z,v.w);static JArray Q(Quaternion q)=>new JArray(q.x,q.y,q.z,q.w);static JArray C(Color c)=>new JArray(c.r,c.g,c.b,c.a);
   static void Write(BinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}static void Write(BinaryWriter w,Vector4 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}static void Write(BinaryWriter w,Vector2 v){w.Write(v.x);w.Write(v.y);}
  }
 }
}

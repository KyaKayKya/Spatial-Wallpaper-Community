using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Execution {
 public sealed class UnityOwnedHost:IVmHost,IDisposable {
  readonly AdmittedBuild build;readonly OwnedDomain domain;readonly SpatialWallpaper.Sdk.Capabilities.CapabilityBindings capabilities;
  readonly HashSet<OwnedHandle> createdResources=new HashSet<OwnedHandle>();
  readonly Dictionary<string,OwnedHandle> resources=new Dictionary<string,OwnedHandle>(StringComparer.Ordinal);
  readonly Dictionary<string,AssetBundle> bundles=new Dictionary<string,AssetBundle>(StringComparer.Ordinal);
  readonly ResourceInventory inventory;readonly RenderRetirementQueue retirement;
  readonly Dictionary<GameObject,NativeReservation> nodeCosts=new Dictionary<GameObject,NativeReservation>();
  readonly Dictionary<AssetBundle,NativeReservation> bundleCosts=new Dictionary<AssetBundle,NativeReservation>();
  readonly ResourceLease hostMemory;readonly JObject content;
  int shadowAtlasMask;
  UnityPresentationState presentation;
  WindowProjection view;
  public void ConfigureView(WindowProjection projection){if(disposed||!projection)throw new InvalidOperationException("view-unavailable");view=projection;}
  internal bool UsesPostProcessing=>presentation!=null&&presentation.PostProcessing;
  public void ConfigurePresentation(Camera camera){
   if(disposed||!camera)throw new InvalidOperationException("presentation-camera-unavailable");
   var additional=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(!additional)throw new InvalidOperationException("presentation-camera-additional-data-required");
   additional.renderPostProcessing=true;presentation?.Configure(camera);
  }
  internal bool UsesMainShadowAtlas=>(shadowAtlasMask&1)!=0;
  internal bool UsesAdditionalShadowAtlas=>(shadowAtlasMask&2)!=0;
  readonly Dictionary<int,GameObject[]> graphs=new Dictionary<int,GameObject[]>();VmRuntime runtime;bool disposed;
  sealed class PrefabDefinition {public readonly JArray Nodes;public PrefabDefinition(JArray nodes){Nodes=nodes;}}
  readonly Dictionary<string,OwnedHandle> prefabs=new Dictionary<string,OwnedHandle>(StringComparer.Ordinal);int nextScope=1;
#if UNITY_EDITOR
  internal static Action<int> GraphBuildProbe;
#endif
  public GameObject Root{get;private set;}
  public UnityOwnedHost(AdmittedBuild admitted,OwnedDomain owner,SpatialWallpaper.Sdk.Capabilities.CapabilityBindings services=null){
   build=admitted??throw new ArgumentNullException(nameof(content));domain=owner??throw new ArgumentNullException(nameof(owner));capabilities=services;
   if(build.IsNativeScript)throw new InvalidOperationException("native-script-requires-mirror-host");
   if(Application.platform!=RuntimePlatform.WindowsEditor&&Application.platform!=RuntimePlatform.WindowsPlayer)throw new PlatformNotSupportedException("schema3-windows-unverified-platform");
   domain.AttachBuild(build);
   hostMemory=domain.Ledger.Reserve(new ResourceCost(checked(4096+build.ContentCloningBytes*2+domain.Ledger.Limit.Objects*128),0,0,0));
   try{
    content=build.Content;hostMemory.Commit();
    // Include every admitted prefab: a script may instantiate its shadow lights later.
    // Author APIs cannot change light type/shadow settings or add native components.
    foreach(var nodes in new[]{(JArray)content["nodes"]}.Concat(((JArray)content["prefabs"]).Select(prefab=>(JArray)prefab["nodes"])))
     foreach(JObject node in nodes)foreach(JObject component in (JArray)node["components"])
      if((string)component["kind"]=="light"&&(int)component["config"]["shadows"]!=0)shadowAtlasMask|=(int)component["config"]["type"]==(int)LightType.Directional?1:2;
    retirement=new RenderRetirementQueue(domain.Ledger,(int)domain.Ledger.Limit.Objects,value=>!(UnityEngine.Object)value);
    inventory=new ResourceInventory(domain,retirement,value=>Destroy((UnityEngine.Object)value));
    foreach(JObject prefab in (JArray)content["prefabs"])prefabs.Add((string)prefab["id"],domain.Register("prefab",new PrefabDefinition((JArray)prefab["nodes"])));Root=new GameObject("SDK content");Root.SetActive(false);LoadResources();graphs.Add(0,BuildGraph((JArray)content["nodes"],Root.transform));}
   catch{Dispose();throw;}
  }
  public void Bind(VmRuntime execution){if(runtime!=null)throw new InvalidOperationException("host-already-bound");runtime=execution;Root.SetActive(true);}
  // Call only after a real render readback completed. Author code has no binding to this method.
  public void SubmittedRender(long sequence){if(disposed)throw new InvalidOperationException("host-stopped");retirement.AdvanceSubmittedRender(sequence);}
  public void CompletedRender(long sequence){if(disposed)throw new InvalidOperationException("host-stopped");retirement.AdvanceCompletedRender(sequence);}
  object ResourceNative(OwnedHandle value,string kind)=>inventory.Native(value,kind);
  void RetireNode(GameObject value){
   if(nodeCosts.TryGetValue(value,out var lease)){nodeCosts.Remove(value);lease.Retire(retirement,value,native=>Destroy((UnityEngine.Object)native));}
   else if(value)Destroy(value);
  }
  void LoadResources(){
   var definitions=(JArray)content["resources"];
   foreach(JObject definition in definitions){
    string kind=(string)definition["kind"],id=(string)definition["id"];
    if(kind=="material")continue;
    byte[] bytes=build.BorrowFile((string)definition["file"]);
    if(kind=="sound"){var clip=new SoundClipData(bytes);new ManagedReservation(domain.Ledger,checked(bytes.Length+256L)).Bind(clip);resources.Add(id,domain.Register("sound",clip));continue;}
    if(kind=="shader"){
     retirement.RequireCapacity(1);var lease=NativeReservation.Reserve(domain.Ledger,ResourceEstimates.ShaderBundle(bytes.Length,build.ShaderDecodedBytes((string)definition["file"])));AssetBundle bundle=null;
     try{bundle=AssetBundle.LoadFromMemory(bytes);if(!bundle)throw new InvalidDataException("shader-bundle-load-failed");lease.BindManagedOwner(bundle);bundles.Add(id,bundle);bundleCosts.Add(bundle,lease);lease.Commit();}
     catch{lease.Retire(retirement,bundle,value=>((AssetBundle)value).Unload(true));throw;}
    }else{
     var cost=kind=="mesh"?ResourceEstimates.BorrowedMesh(bytes):ResourceEstimates.BorrowedTexture(bytes);
     resources.Add(id,inventory.Create(kind,cost,born=>kind=="mesh"?(UnityEngine.Object)NativeResourceReader.Mesh(bytes,value=>born(value)):NativeResourceReader.Texture(bytes,value=>born(value))));
    }
   }
   foreach(JObject definition in definitions.Where(d=>(string)d["kind"]=="material")){
    string file=(string)definition["file"];var parsing=AllocationFrame.Begin(domain.Ledger);parsing.Charge(build.MaterialParsingBytes(file));
    try{
    var spec=JsonData.Parse(new System.Text.UTF8Encoding(false,true).GetString(build.BorrowFile(file)));string shaderName=(string)spec["shader"],bundleName=(string)spec["bundle"];
    var shader=bundleName.Length==0?Shader.Find(shaderName):bundles[bundleName].LoadAllAssets<Shader>().SingleOrDefault(s=>s.name==shaderName);
    if(!shader||!shader.isSupported)throw new InvalidDataException("unsupported-admitted-shader");
    var handle=inventory.Create("material",ResourceEstimates.Material(),()=>new Material(shader));
    var material=(Material)inventory.Native(handle,"material");resources.Add((string)definition["id"],handle);
    foreach(var property in ((JObject)spec["properties"]).Properties()){
     var value=(JObject)property.Value;if(!material.HasProperty(property.Name))throw new InvalidDataException("shader-property-drift");
     switch((string)value["type"]){
      case "f32":MaterialProperty(material,property.Name,UnityEngine.Rendering.ShaderPropertyType.Float);material.SetFloat(property.Name,(float)value["value"]);break;
      case "color":MaterialProperty(material,property.Name,UnityEngine.Rendering.ShaderPropertyType.Color);material.SetColor(property.Name,Color(value["value"]));break;
      case "texture":
       int index=MaterialProperty(material,property.Name,UnityEngine.Rendering.ShaderPropertyType.Texture);var resource=resources[(string)value["value"]];
       var dimension=resource.Kind=="cubemap"?UnityEngine.Rendering.TextureDimension.Cube:UnityEngine.Rendering.TextureDimension.Tex2D;
       if(material.shader.GetPropertyTextureDimension(index)!=dimension)throw new InvalidDataException("material-texture-dimension-drift");
       material.SetTexture(property.Name,(Texture)ResourceNative(resource,resource.Kind));break;
      default:throw new InvalidDataException("unbound-material-property");
     }
    }
    }finally{GC.KeepAlive(parsing);}
   }
  }
  GameObject[] BuildGraph(JArray nodes,Transform parent,bool activate=true){
   var layout=new ManagedReservation(domain.Ledger,checked(1024L+nodes.Count*16L),1);
   var temporary=AllocationFrame.Begin(domain.Ledger);temporary.Charge(checked(1024L+nodes.Count*16L));
   var result=new List<GameObject>(nodes.Count);
   try{
   foreach(JObject node in nodes){
    int nativeComponents=((JArray)node["components"]).Count+((JArray)node["components"]).Count(component=>(string)component["kind"]=="light");
    retirement.RequireCapacity(1);var nodeCost=NativeReservation.Reserve(domain.Ledger,ResourceEstimates.Node(nativeComponents));
    GameObject go=null;
    try{go=new GameObject((string)node["id"]);nodeCost.BindManagedOwner(go);nodeCosts.Add(go,nodeCost);nodeCost.Commit();}
    catch{nodeCost.Retire(retirement,go,value=>Destroy((UnityEngine.Object)value));throw;}
    result.Add(go);go.SetActive(false);go.transform.SetParent(parent,false);
#if UNITY_EDITOR
    GraphBuildProbe?.Invoke(result.Count-1);
#endif
    int index=(int)node["parent"];go.transform.SetParent(index<0?parent:result[index].transform,false);
    foreach(JObject component in (JArray)node["components"]){
     var config=(JObject)component["config"];
     switch((string)component["kind"]){
      case "transform":go.transform.localPosition=Vector(config["position"]);var q=config["rotation"];go.transform.localRotation=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]);go.transform.localScale=Vector(config["scale"]);break;
      case "mesh-renderer":var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=config["mesh"].Type==JTokenType.Null?null:(Mesh)ResourceNative(resources[(string)config["mesh"]],"mesh");var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=config["material"].Type==JTokenType.Null?null:(Material)ResourceNative(resources[(string)config["material"]],"material");break;
      case "light":
       var light=go.AddComponent<Light>();light.type=(LightType)(int)config["type"];light.color=Color(config["color"]);light.intensity=(float)config["intensity"];
       // Modern content stays in author units; projection converts physical display/eye units.
       light.range=(float)config["range"];light.spotAngle=(float)config["spot"];light.innerSpotAngle=(float)config["innerSpot"];
       light.shadows=(LightShadows)(int)config["shadows"];light.shadowStrength=(float)config["shadowStrength"];light.shadowBias=(float)config["bias"];light.shadowNormalBias=(float)config["normalBias"];light.shadowNearPlane=(float)config["near"];
       go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().usePipelineSettings=(bool)config["usePipelineSettings"];break;
      case "presentation":if(presentation!=null)throw new InvalidDataException("duplicate-presentation");presentation=new UnityPresentationState(config,go,domain.Ledger);break;
      case "script":break;default:throw new InvalidDataException("unbound-component");
     }
    }
   }
   if(activate)foreach(var go in result)go.SetActive(true);var array=result.ToArray();layout.Bind(array);return array;
   }catch{foreach(var go in result)if(go){go.SetActive(false);RetireNode(go);}throw;}
   finally{GC.KeepAlive(temporary);GC.KeepAlive(layout);}
  }
  public bool Supports(string member)=>RenderingBindings.Supports(member)||capabilities!=null&&SpatialWallpaper.Sdk.Capabilities.CapabilityBindings.Supports(member);
  public object Resolve(string type,JToken value,int scope){
   if(type=="prefab")return prefabs[(string)value];
   if(type=="node"){var go=graphs[scope][(int)value];if(!go)throw new InvalidOperationException("destroyed-node");return domain.Register(type,go);}
   if(type=="light"){var light=graphs[scope][(int)value].GetComponent<Light>();if(!light)throw new InvalidOperationException("missing-owned-light");return domain.Register(type,light);}
   if(resources.TryGetValue((string)value,out var resource)){domain.RequireType(resource,type);return resource;}
   throw new InvalidOperationException("unresolved-owned-reference");
  }
  T Native<T>(object receiver,string kind)where T:UnityEngine.Object{var value=(T)(kind=="node"||kind=="light"?domain.Unwrap((OwnedHandle)receiver,kind):inventory.Native((OwnedHandle)receiver,kind));if(!value)throw new InvalidOperationException("destroyed-native-reference");return value;}
  void Mutate(ResourceCost estimate,Action operation){
   retirement.RequireCapacity(1);var lease=NativeReservation.Reserve(domain.Ledger,estimate);
   try{operation();lease.Commit();}
   finally{lease.Retire(retirement,null,value=>{});}
  }
  OwnedHandle Created(OwnedHandle value){createdResources.Add(value);return value;}
  void ReleaseCreated(OwnedHandle value){
   domain.Check(value);if(!createdResources.Contains(value))throw new InvalidOperationException("release-requires-created-resource");
   inventory.Release(value);createdResources.Remove(value);
  }
  static ResourceCost BridgeCopy(long bytes)=>new ResourceCost(checked(256+bytes),checked(1024+bytes),checked(1024+bytes),0);
  public object Call(string member,object receiver,object[] args){
   if(disposed||!Supports(member))throw new InvalidOperationException("host-member-unavailable");
   if(capabilities!=null&&SpatialWallpaper.Sdk.Capabilities.CapabilityBindings.Supports(member))return capabilities.Call(member,args);
   switch(member){
    case "SpatialWallpaper.Sdk::Sound::PlayMusic(UnityEngine.AudioClip,System.Single)->System.Void":
     if(capabilities==null)throw new InvalidOperationException("sound-unavailable");capabilities.Sound.PlayMusic((SoundClipData)domain.Unwrap((OwnedHandle)args[0],"sound"),(float)args[1]);return null;
    case "SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void":
     if(capabilities==null)throw new InvalidOperationException("sound-unavailable");capabilities.Sound.PlayEffect((SoundClipData)domain.Unwrap((OwnedHandle)args[0],"sound"),(float)args[1]);return null;
    case "SpatialWallpaper.Sdk::Sound::StopMusic()->System.Void":capabilities?.Sound.StopMusic();return null;
    case "SpatialWallpaper.Sdk::Sound::StopEffects()->System.Void":capabilities?.Sound.StopEffects();return null;
    case "SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3":
     if(!view)throw new InvalidOperationException("view-unavailable");var eye=view.EyeScreen;return new VectorValue("vector3",eye.x,eye.y,eye.z);
    case "SpatialWallpaper.Sdk::View::GetSize()->UnityEngine.Vector3":
     if(!view)throw new InvalidOperationException("view-unavailable");return new VectorValue("vector3",view.displayWidth,view.displayHeight,0);
    case "SpatialWallpaper.Sdk::Resources::Release(Mesh)->System.Void":
    case "SpatialWallpaper.Sdk::Resources::Release(Texture)->System.Void":
    case "SpatialWallpaper.Sdk::Resources::Release(Material)->System.Void":ReleaseCreated((OwnedHandle)args[0]);return null;
    case "SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node":return Instantiate((OwnedHandle)args[0]);
    case "SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void":if(args[0]!=null)DestroyNode(Native<GameObject>(args[0],"node"));return null;
    case "UnityEngine.CoreModule::UnityEngine.Transform::Rotate(System.Single,System.Single,System.Single)->System.Void":var rotated=Native<GameObject>(receiver,"node");Mutate(BridgeCopy(0),()=>rotated.transform.Rotate((float)args[0],(float)args[1],(float)args[2]));return null;
    case "UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3":var v=Native<GameObject>(receiver,"node").transform.localPosition;return new VectorValue("vector3",v.x,v.y,v.z);
    case "UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void":var p=(VectorValue)args[0];var moved=Native<GameObject>(receiver,"node");Mutate(BridgeCopy(0),()=>moved.transform.localPosition=new Vector3(p.X,p.Y,p.Z));return null;
    case "UnityEngine.CoreModule::UnityEngine.Light::get_intensity()->System.Single":return Native<Light>(receiver,"light").intensity;
    case "UnityEngine.CoreModule::UnityEngine.Light::set_intensity(System.Single)->System.Void":var light=Native<Light>(receiver,"light");Mutate(BridgeCopy(0),()=>light.intensity=(float)args[0]);return null;
    case "UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void":var mf=Native<Material>(receiver,"material");MaterialProperty(mf,(string)args[0],UnityEngine.Rendering.ShaderPropertyType.Float);Mutate(BridgeCopy(0),()=>mf.SetFloat((string)args[0],(float)args[1]));return null;
    case "UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void":var mc=Native<Material>(receiver,"material");MaterialProperty(mc,(string)args[0],UnityEngine.Rendering.ShaderPropertyType.Color);var color=(VectorValue)args[1];Mutate(BridgeCopy(0),()=>mc.SetColor((string)args[0],new Color(color.X,color.Y,color.Z,color.W)));return null;
    case "UnityEngine.CoreModule::UnityEngine.Mesh::Clear()->System.Void":ReplaceMesh((OwnedHandle)receiver,0,0,copy=>copy.Clear());return null;
    case "UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void":
     SetVertices((OwnedHandle)receiver,(OwnedArray)args[0]);return null;
    case "UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void":
     SetTriangles((OwnedHandle)receiver,(OwnedArray)args[0]);return null;
    case "UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateNormals()->System.Void":
     var normalMesh=Native<Mesh>(receiver,"mesh");ReplaceMesh((OwnedHandle)receiver,normalMesh.vertexCount,IndexCount(normalMesh),copy=>copy.RecalculateNormals());return null;
    case "UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateBounds()->System.Void":
     var boundMesh=Native<Mesh>(receiver,"mesh");ReplaceMesh((OwnedHandle)receiver,boundMesh.vertexCount,IndexCount(boundMesh),copy=>copy.RecalculateBounds());return null;
    case "SpatialWallpaper.Sdk::Resources::BindMesh(Node,Mesh)->System.Void":
     var filter=Native<GameObject>(args[0],"node").GetComponent<MeshFilter>();if(!filter)throw new InvalidOperationException("missing-owned-mesh-filter");
     var bound=args[1]==null?null:Native<Mesh>(args[1],"mesh");Mutate(BridgeCopy(0),()=>filter.sharedMesh=bound);return null;
    case "UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void":var applied=Native<Texture2D>(receiver,"texture");var upload=ResourceEstimates.Texture(applied.width,applied.height,(int)applied.format,1,applied.mipmapCount);Mutate(BridgeCopy((upload.NativeBytes-4096)/2),()=>applied.Apply());return null;
    case "UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void":
     var pixelTexture=Native<Texture2D>(receiver,"texture");int x=(int)args[0],y=(int)args[1];var pixel=(VectorValue)args[2];
     if(x<0||y<0||x>=pixelTexture.width||y>=pixelTexture.height)throw new InvalidOperationException("texture-pixel-bounds");
     if(!pixelTexture.isReadable||pixelTexture.format!=TextureFormat.RGBA32&&pixelTexture.format!=TextureFormat.RGBAHalf)throw new InvalidOperationException("unsupported-texture-pixel-format");
     if(pixelTexture.format==TextureFormat.RGBAHalf&&(Math.Abs(pixel.X)>65504||Math.Abs(pixel.Y)>65504||Math.Abs(pixel.Z)>65504||Math.Abs(pixel.W)>65504))throw new InvalidOperationException("texture-half-component-range");
     Mutate(BridgeCopy(16),()=>pixelTexture.SetPixel(x,y,new Color(pixel.X,pixel.Y,pixel.Z,pixel.W)));return null;
    case "SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh":return Created(inventory.Create("mesh",ResourceEstimates.Mesh(0,0),()=>new Mesh()));
    case "SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture":int width=(int)args[0],height=(int)args[1];return Created(inventory.Create("texture",ResourceEstimates.Texture(width,height,4,1,1),()=>new Texture2D(width,height,TextureFormat.RGBA32,false,true)));
    case "SpatialWallpaper.Sdk::Resources::CreateMaterial(Material)->Material":var original=Native<Material>(args[0],"material");return Created(inventory.Create("material",ResourceEstimates.Material(),()=>new Material(original)));
    default:throw new InvalidOperationException("host-member-not-bound");
   }
  }
  static int IndexCount(Mesh mesh){long count=0;for(int i=0;i<mesh.subMeshCount;i++)count=checked(count+(long)mesh.GetIndexCount(i));return checked((int)count);}
  void ReplaceMesh(OwnedHandle handle,int vertices,int indices,Action<Mesh> edit){
   var old=Native<Mesh>(handle,"mesh");
   // Clone retains every admitted channel, submesh and layout. Both old and new costs overlap until retirement.
   int capacity=Math.Max(vertices,old.vertexCount),indexCapacity=Math.Max(indices,IndexCount(old));
   inventory.Replace(handle,ResourceEstimates.Mesh(capacity,indexCapacity),BridgeCopy(checked(256L+capacity*32L+indexCapacity*4L)),(previous,born)=>{
    var copy=UnityEngine.Object.Instantiate((Mesh)previous);born(copy);edit(copy);return copy;
   },(previous,next)=>{foreach(var node in nodeCosts.Keys)if(node){var target=node.GetComponent<MeshFilter>();if(target&&target.sharedMesh==(Mesh)previous)target.sharedMesh=(Mesh)next;}});
  }
  void SetVertices(OwnedHandle handle,OwnedArray values){
   domain.RequireType(values,"vector3[]");if(values==null)throw new InvalidOperationException("null-mesh-vertices");
   var mesh=Native<Mesh>(handle,"mesh");
   if(mesh.vertexCount!=0&&values.Count!=mesh.vertexCount)throw new InvalidOperationException("mesh-resize-requires-clear");
   for(int i=0;i<values.Count;i++){var v=(VectorValue)domain.Get(values,i);if(Math.Abs(v.X)>100000||Math.Abs(v.Y)>100000||Math.Abs(v.Z)>100000)throw new InvalidOperationException("mesh-vertex-range");}
   ReplaceMesh(handle,values.Count,IndexCount(mesh),copy=>{
    var vertices=new Vector3[values.Count];for(int i=0;i<vertices.Length;i++){var v=(VectorValue)domain.Get(values,i);vertices[i]=new Vector3(v.X,v.Y,v.Z);}copy.vertices=vertices;
   });
  }
  void SetTriangles(OwnedHandle handle,OwnedArray values){
   domain.RequireType(values,"i32[]");if(values==null||values.Count%3!=0)throw new InvalidOperationException("mesh-triangle-count");
   var mesh=Native<Mesh>(handle,"mesh");
   for(int i=0;i<values.Count;i++){int index=(int)domain.Get(values,i);if(index<0||index>=mesh.vertexCount)throw new InvalidOperationException("mesh-triangle-index");}
   ReplaceMesh(handle,mesh.vertexCount,values.Count,copy=>{var indices=new int[values.Count];for(int i=0;i<indices.Length;i++)indices[i]=(int)domain.Get(values,i);copy.triangles=indices;});
  }
  OwnedHandle Instantiate(OwnedHandle template){
   if(runtime==null)throw new InvalidOperationException("unbound-runtime");
   var definition=(PrefabDefinition)domain.Unwrap(template,"prefab");long checkpoint=domain.Checkpoint();int scope=checked(nextScope++);GameObject[] graph=null;
   try{
    graph=BuildGraph(definition.Nodes,Root.transform,false);graphs.Add(scope,graph);
    runtime.BindGraph(definition.Nodes,scope);
    foreach(var node in graph)node.SetActive(true);
    return domain.Register("node",graph[0]);
   }catch{
    if(graph!=null){runtime.DetachNodes(scope,new HashSet<int>(Enumerable.Range(0,graph.Length)));foreach(var node in graph)if(node)RetireNode(node);}
    graphs.Remove(scope);domain.Rollback(checkpoint);throw;
   }
  }
  void DestroyNode(GameObject target){
   var allocation=AllocationFrame.Begin(domain.Ledger);allocation.Charge(checked(1024L+graphs.Count*64L+nodeCosts.Count*64L));
   try{
   var emptyScopes=new List<int>();
   foreach(var graph in graphs){
    var indices=new HashSet<int>(Enumerable.Range(0,graph.Value.Length).Where(i=>graph.Value[i]&&(graph.Value[i]==target||graph.Value[i].transform.IsChildOf(target.transform))));
    runtime.DetachNodes(graph.Key,indices);
    foreach(int index in indices)graph.Value[index]=null;
    if(graph.Key!=0&&graph.Value.All(node=>!node))emptyScopes.Add(graph.Key);
   }
   foreach(int scope in emptyScopes)graphs.Remove(scope);
   domain.InvalidateHandles(handle=>handle.Native is GameObject node&&node&&(node==target||node.transform.IsChildOf(target.transform))||handle.Native is Component component&&component&&(component.gameObject==target||component.transform.IsChildOf(target.transform)));
   target.SetActive(false);
   var removed=nodeCosts.Keys.Where(node=>node&&(node==target||node.transform.IsChildOf(target.transform))).ToArray();
   foreach(var node in removed)RetireNode(node);
   }finally{GC.KeepAlive(allocation);}
  }
  static int MaterialProperty(Material material,string name,UnityEngine.Rendering.ShaderPropertyType expected){
   var shader=material.shader;if(!shader)throw new InvalidOperationException("material-shader-unavailable");
   int index=shader.FindPropertyIndex(name);if(index<0)throw new InvalidOperationException("missing-material-property");
   var actual=shader.GetPropertyType(index);
   if(actual!=expected&&!(expected==UnityEngine.Rendering.ShaderPropertyType.Float&&actual==UnityEngine.Rendering.ShaderPropertyType.Range))throw new InvalidOperationException("material-property-type");
   return index;
  }
  static Vector3 Vector(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
  static Color Color(JToken v)=>new Color((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
  public void Dispose(){
   if(disposed)return;disposed=true;if(Root)Root.SetActive(false);presentation?.Dispose();presentation=null;if(Root)Destroy(Root);
   if(inventory!=null)inventory.Dispose();
   if(retirement!=null){
    foreach(var node in nodeCosts.Keys.ToArray())RetireNode(node);
    foreach(var pair in bundleCosts)pair.Value.Retire(retirement,pair.Key,value=>((AssetBundle)value).Unload(true));
    retirement.CloseWhenDrained();
   }
   bundleCosts.Clear();bundles.Clear();hostMemory?.Dispose();domain.Dispose();graphs.Clear();
  }
  static void Destroy(UnityEngine.Object value){if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
 }
}

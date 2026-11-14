using System;using System.IO;using UnityEngine;using UnityEngine.Rendering;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public sealed class StageDResourceObserver:MonoBehaviour{
 ResourceLedger ledger;OwnedDomain domain;UnityOwnedHost host;ResourceInventory partialInventory;RenderRetirementQueue partialRetirement;Texture2D partial;RenderTexture target;OwnedHandle dynamicMesh;long before,pending,completed;bool reading,done,pixelOnGpu,halfPixelOnGpu,meshOnGpu;int started,scopesAfterCycles,callbacksAfterCycles;
 void Start(){try{
  Application.runInBackground=true;if(!SystemInfo.supportsAsyncGPUReadback)throw new Exception("GPU-readback-unavailable");
  string output=Environment.GetEnvironmentVariable("SDK_D_OUTPUT");
  var build=Admission.Admit(File.ReadAllBytes(Path.Combine(output,"probe.spatial")),"windows-x86_64");
  ledger=new ResourceLedger(new ResourceCost(16*1024*1024,64*1024*1024,64*1024*1024,1024));domain=new OwnedDomain(Guid.NewGuid(),1,ledger);host=new UnityOwnedHost(build,domain);var vm=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(vm);vm.Start();
  var compressed=host.Resolve("texture",new JValue("Compressed"),0);bool formatRejected=false;
  try{host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",compressed,new object[]{0,0,new VectorValue("color",1,0,0,1)});}catch(InvalidOperationException error){formatRejected=error.Message=="unsupported-texture-pixel-format";}
  if(!formatRejected)throw new Exception("compressed-pixel-write-silently-accepted");
  var material=host.Resolve("material",new JValue("Material"),0);bool propertyRejected=false;
  try{host.Call("UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void",material,new object[]{"_BaseMap",0.5f});}catch(InvalidOperationException error){propertyRejected=error.Message=="material-property-type";}
  if(!propertyRejected)throw new Exception("material-property-type-silently-accepted");
  bool wrongColor=false;try{host.Call("UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void",material,new object[]{"_BaseMap",new VectorValue("color",1,0,0,1)});}catch(InvalidOperationException error){wrongColor=error.Message=="material-property-type";}if(!wrongColor)throw new Exception("material-color-to-texture-silently-accepted");
  int count=UnityEngine.Resources.FindObjectsOfTypeAll<Texture2D>().Length;bool rejected=false;
  try{host.Call("SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture",null,new object[]{8192,8192});}catch(ResourceBudgetException){rejected=true;}
  if(!rejected||UnityEngine.Resources.FindObjectsOfTypeAll<Texture2D>().Length!=count)throw new Exception("texture-budget-allocated-before-rejection");
  before=ledger.Used.NativeBytes;
  var unwrap=typeof(UnityOwnedHost).GetMethod("ResourceNative",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
  var mesh=(OwnedHandle)host.Call("SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh",null,new object[0]);
  dynamicMesh=mesh;
  var points=domain.Array("vector3",new object[]{new VectorValue("vector3",-1,-1,0),new VectorValue("vector3",0,1,0),new VectorValue("vector3",1,-1,0)});
  var triangles=domain.Array("i32",new object[]{0,1,2});
  host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void",mesh,new object[]{points});
  host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void",mesh,new object[]{triangles});
  host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateNormals()->System.Void",mesh,new object[0]);
  host.Call("SpatialWallpaper.Sdk::Resources::BindMesh(Node,Mesh)->System.Void",null,new object[]{host.Resolve("node",new JValue(0),0),mesh});
  var meshFilter=host.Root.transform.GetChild(0).GetComponent<MeshFilter>();var previousMesh=meshFilter.sharedMesh;
  var uv=new[]{new Vector2(0,0),new Vector2(.5f,1),new Vector2(1,0)};previousMesh.uv=uv;
  points=domain.Array("vector3",new object[]{new VectorValue("vector3",-.8f,-.8f,0),new VectorValue("vector3",0,.8f,0),new VectorValue("vector3",.8f,-.8f,0)});
  host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void",mesh,new object[]{points});
  var changed=meshFilter.sharedMesh;if(ReferenceEquals(previousMesh,changed)||changed.vertexCount!=3||changed.uv.Length!=3||changed.uv[1]!=uv[1]||changed.triangles[2]!=2||changed.normals.Length!=3)throw new Exception("mesh-replacement-drift-or-binding-not-updated");
  long meshBeforeFailure=ledger.Used.NativeBytes;
  foreach(var bad in new[]{new object[]{0,1,3},new object[]{0,1}}){bool denied=false;try{host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void",mesh,new object[]{domain.Array("i32",bad)});}catch(InvalidOperationException){denied=true;}if(!denied||!ReferenceEquals(meshFilter.sharedMesh,changed)||ledger.Used.NativeBytes!=meshBeforeFailure)throw new Exception("invalid-mesh-indices-mutated-or-allocated");}
  bool foreign=false;using(var other=new OwnedDomain(Guid.NewGuid(),1)){try{host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void",mesh,new object[]{other.Array("vector3",new object[]{new VectorValue("vector3",0,0,0)})});}catch(InvalidOperationException){foreign=true;}}
  if(!foreign||!ReferenceEquals(meshFilter.sharedMesh,changed))throw new Exception("foreign-mesh-array-accepted");
  bool range=false;try{host.Call("UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void",mesh,new object[]{domain.Array("vector3",new object[]{new VectorValue("vector3",100001,0,0),new VectorValue("vector3",0,0,0),new VectorValue("vector3",0,0,0)})});}catch(InvalidOperationException){range=true;}if(!range)throw new Exception("mesh-vertex-range-accepted");
  var prefab=host.Resolve("prefab",new JValue("Cycle"),0);
  for(int i=0;i<64;i++){var instance=host.Call("SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node",null,new object[]{prefab});host.Call("SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void",null,new[]{instance});}
  var graphs=(System.Collections.IDictionary)typeof(UnityOwnedHost).GetField("graphs",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(host);
  scopesAfterCycles=graphs.Count;callbacksAfterCycles=((System.Collections.ICollection)typeof(VmRuntime).GetField("callbacks",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(vm)).Count;
  if(scopesAfterCycles!=1||callbacksAfterCycles!=1)throw new Exception("destroyed-prefab-retained-object-graphs-or-callbacks:"+scopesAfterCycles+":"+callbacksAfterCycles);
  var rawMaterial=(Material)unwrap.Invoke(host,new object[]{material,"material"});
  host.Call("UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void",material,new object[]{"_Surface",0.5f});
  host.Call("UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void",material,new object[]{"_BaseColor",new VectorValue("color",0.25f,0.5f,0.75f,1)});
  if(rawMaterial.GetFloat("_Surface")!=0.5f||rawMaterial.GetColor("_BaseColor")!=new Color(0.25f,0.5f,0.75f,1))throw new Exception("material-mutation-drift");
  var half=host.Resolve("texture",new JValue("Half"),0);host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",half,new object[]{1,1,new VectorValue("color",0.125f,0.25f,0.5f,1)});host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void",half,new object[0]);
  bool halfRangeRejected=false;try{host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",half,new object[]{3,3,new VectorValue("color",100000,0,0,1)});}catch(InvalidOperationException error){halfRangeRejected=error.Message=="texture-half-component-range";}if(!halfRangeRejected)throw new Exception("half-pixel-overflow-silently-accepted");
  var rawHalf=(Texture2D)unwrap.Invoke(host,new object[]{half,"texture"});
  AsyncGPUReadback.Request(rawHalf,0,TextureFormat.RGBA32,request=>{if(done)return;try{if(request.hasError)throw new Exception("half-texture-GPU-readback-failed");var pixels=request.GetData<Color32>();if(pixels.Length!=16||Math.Abs(pixels[5].r-32)>1||Math.Abs(pixels[5].g-64)>1||Math.Abs(pixels[5].b-128)>1||pixels[5].a!=255)throw new Exception("half-texture-GPU-pixel-drift");halfPixelOnGpu=true;}catch(Exception error){Fail(error);}});
  partialRetirement=new RenderRetirementQueue(ledger,16,value=>!(UnityEngine.Object)value);partialInventory=new ResourceInventory(domain,partialRetirement,value=>Destroy((UnityEngine.Object)value));
  bool partialRejected=false;
  try{partialInventory.Create("texture",ResourceEstimates.Texture(8,8,4,1,1),born=>{partial=new Texture2D(8,8,TextureFormat.RGBA32,false,true);born(partial);throw new InvalidOperationException("fixture-partial-texture-configuration");});}
  catch(InvalidOperationException error){partialRejected=error.Message=="fixture-partial-texture-configuration";}
  if(!partialRejected||ReferenceEquals(partial,null)||ledger.Used.NativeBytes<=before)throw new Exception("partial-native-not-tracked-and-charged");
  var texture=(OwnedHandle)host.Call("SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture",null,new object[]{32,32});
  host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",texture,new object[]{0,0,new VectorValue("color",0.25f,0.5f,0.75f,1)});
  var rawTexture=(Texture2D)unwrap.Invoke(host,new object[]{texture,"texture"});
  var pixel=rawTexture.GetPixel(0,0);if(Mathf.Abs(pixel.r-0.25f)>1f/255||Mathf.Abs(pixel.g-0.5f)>1f/255||Mathf.Abs(pixel.b-0.75f)>1f/255)throw new Exception("dynamic-pixel-write-drift");
  bool coordinateRejected=false;try{host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void",texture,new object[]{32,0,new VectorValue("color",1,0,0,1)});}catch(InvalidOperationException){coordinateRejected=true;}
  if(!coordinateRejected||rawTexture.GetPixel(0,0)!=pixel)throw new Exception("out-of-range-pixel-write-mutated-texture");
  host.Call("UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void",texture,new object[0]);
  AsyncGPUReadback.Request(rawTexture,0,TextureFormat.RGBA32,request=>{
   if(done)return;
   try{if(request.hasError)throw new Exception("dynamic-texture-GPU-readback-failed");var pixels=request.GetData<Color32>();if(pixels.Length!=1024||Math.Abs(pixels[0].r-64)>1||Math.Abs(pixels[0].g-128)>1||Math.Abs(pixels[0].b-191)>1||pixels[0].a!=255)throw new Exception("dynamic-texture-GPU-pixel-drift");pixelOnGpu=true;}catch(Exception error){Fail(error);}
  });
  host.Call("SpatialWallpaper.Sdk::Resources::Release(Texture)->System.Void",null,new object[]{texture});
  bool stale=false;try{domain.Check(texture);}catch(InvalidOperationException){stale=true;}if(!stale)throw new Exception("released-resource-still-live");
  pending=ledger.Used.NativeBytes;if(pending<=before)throw new Exception("native-destroy-refunded-immediately");
  target=new RenderTexture(16,16,0,RenderTextureFormat.ARGB32);target.Create();var camera=new GameObject("ReadbackCamera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-3);camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.cyan;
  started=Time.frameCount;RenderPipelineManager.endFrameRendering+=EndFrame;
 }catch(Exception error){Fail(error);}}
 void EndFrame(ScriptableRenderContext context,Camera[] cameras){
  if(done||reading)return;reading=true;
  try{AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32,request=>{
   reading=false;if(done)return;
   try{
    if(request.hasError||request.GetData<byte>().Length!=16*16*4)throw new Exception("GPU-readback-failed");
    if(!meshOnGpu){var center=request.GetData<Color32>()[8*16+8];if(center.r==0&&center.g==255&&center.b==255)throw new Exception("dynamic-mesh-not-rendered");meshOnGpu=true;host.Call("SpatialWallpaper.Sdk::Resources::Release(Mesh)->System.Void",null,new object[]{dynamicMesh});}
    host.CompletedRender(++completed);
    partialRetirement.AdvanceCompletedRender(completed);
    if(completed==1&&ledger.Used.NativeBytes!=pending)throw new Exception("native-refunded-before-safe-readbacks");
    if(completed<4)return;
    if(ledger.Used.NativeBytes!=before)throw new Exception("completed-readbacks-did-not-retire-resource");
    if(partial)throw new Exception("partial-native-texture-not-destroyed");
    if(!pixelOnGpu||!halfPixelOnGpu||!meshOnGpu)throw new Exception("dynamic-resource-not-verified-on-GPU");
    string output=Environment.GetEnvironmentVariable("SDK_D_OUTPUT");
    File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_D_RUN")},{"completed_gpu_readbacks",completed},{"native_before",before},{"native_pending",pending},{"native_after",ledger.Used.NativeBytes},{"preallocation_rejected",true},{"released_handle_rejected",true},{"partial_native_destroyed",true},{"dynamic_mesh_verified_on_GPU",meshOnGpu},{"mesh_atomic_rebind_and_attributes",true},{"mesh_invalid_indices_and_foreign_arrays_rejected",true},{"dynamic_pixel_verified_on_GPU",true},{"pixel_bounds_rejected",true},{"unsupported_pixel_format_rejected",true},{"half_component_range_rejected",true},{"material_property_type_rejected",true},{"half_pixel_verified_on_GPU",true},{"material_mutations_verified",true},{"prefab_cycles",64},{"remaining_graph_scopes",scopesAfterCycles},{"remaining_callbacks",callbacksAfterCycles},{"engine",Application.unityVersion},{"graphics",SystemInfo.graphicsDeviceType.ToString()},{"device",SystemInfo.graphicsDeviceName}}.ToString());done=true;
   }catch(Exception error){Fail(error);}
  });}catch(Exception error){Fail(error);}
 }
 void Update(){if(started!=0&&Time.frameCount-started>2000&&!done)Fail(new Exception("GPU-progress-timeout"));}
 void Fail(Exception error){done=true;File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("SDK_D_OUTPUT"),"error.txt"),error.ToString());}
 void OnDestroy(){RenderPipelineManager.endFrameRendering-=EndFrame;partialInventory?.Dispose();partialRetirement?.CloseWhenDrained();host?.Dispose();ledger?.Dispose();if(target){target.Release();Destroy(target);}}
}

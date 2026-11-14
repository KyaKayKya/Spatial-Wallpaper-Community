using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using UnityEngine;using UnityEngine.Rendering;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Native;using SpatialWallpaper.Sdk.Native.Unity;
namespace SpatialWallpaper.Desktop {
 // Consumes only immutable bytes from NativeExternalPackage admission. The
 // reused loader constructs known Unity types; its bundle gate admits Shader only.
 public sealed class ExternalNativeScene {
  readonly OwnedDomain owner;readonly NativeExternalPackage package;readonly ContentLoader loader=new ContentLoader();readonly ResourceLease allocation;readonly int thread;ContentHandle handle;UnityEngine.Object[] retired;NativeGpuCompletion fence;bool closing,released,failed;
  public Transform Content=>handle?.Root?handle.Root.transform:null;
  public Transform[] ScriptNodes{get;private set;}
  public bool CanDisposeOwner=>released;
  public string Failure{get;private set;}
  public ExternalNativeScene(NativeExternalPackage package,OwnedDomain owner){if(!NativeGpuCompletion.Supported)throw new PlatformNotSupportedException("external-native-GPU-fence-required");this.owner=owner;this.package=package??throw new ArgumentNullException(nameof(package));thread=Thread.CurrentThread.ManagedThreadId;allocation=owner.Ledger.Reserve(package.EstimatedCost);allocation.Commit();}
  void ThreadCheck(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("external-native-main-thread");}
  public async Task LoadAsync(Camera camera,double width,double height,bool keepHeight,CancellationToken cancellation){ThreadCheck();if(handle!=null||closing||failed)throw new InvalidOperationException("external-native-load-state");try{handle=await loader.LoadAsync(new SceneLoadRequest{Package=package.Data,Camera=camera,ViewportWidth=width,ViewportHeight=height,KeepHeight=keepHeight},cancellation);var graph=(Newtonsoft.Json.Linq.JArray)package.Data.Scene["nodes"];var nodes=new Transform[graph.Count];var children=new int[graph.Count+1];for(int i=0;i<nodes.Length;i++){int parent=(int)graph[i]["parent"];Transform owner=parent<0?handle.Root.transform:nodes[parent];nodes[i]=owner.GetChild(children[parent+1]++);}foreach(var renderer in handle.Root.GetComponentsInChildren<MeshRenderer>(true)){renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;}ScriptNodes=nodes;}catch(Exception error){failed=true;Failure=error.Message;/* Unknown failed native constructor state retains its full estimate until render-process exit. */throw;}}
  public async Task CloseAsync(){ThreadCheck();if(closing)return;if(failed)throw new InvalidOperationException("external-native-load-failed-render-process-exit-required");closing=true;if(handle==null){allocation.Dispose();released=true;return;}var objects=new List<UnityEngine.Object>(handle.Assets);objects.Add(handle.Root);foreach(var bundle in handle.Bundles)objects.AddRange(bundle.LoadAllAssets<Shader>());retired=objects.ToArray();await loader.UnloadAsync(handle);ScriptNodes=null;fence=new NativeGpuCompletion(owner);}
  public void PumpRetirement(){ThreadCheck();if(!closing||released||retired==null)return;if(!fence.Passed||retired.Any(value=>value))return;fence.Dispose();allocation.Dispose();retired=null;handle=null;released=true;}
 }
}

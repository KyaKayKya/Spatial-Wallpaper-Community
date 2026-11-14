using System;using UnityEngine;using UnityEngine.Rendering;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Native.Unity {
 // D3D11 has no async-compute fence polling. A four-byte synthetic buffer
 // readback completes in graphics-queue order; no rendered pixels are read.
 public sealed class NativeGpuCompletion:IDisposable {
  GraphicsFence fence;ComputeBuffer sentinel;ResourceLease lease;bool asynchronous,done,error,disposed;
  public static bool Supported=>SystemInfo.supportsGraphicsFence&&SystemInfo.supportsAsyncCompute||SystemInfo.supportsAsyncGPUReadback;
  public NativeGpuCompletion(OwnedDomain owner){if(SystemInfo.supportsGraphicsFence&&SystemInfo.supportsAsyncCompute){fence=Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,SynchronisationStageFlags.AllGPUOperations);return;}if(!SystemInfo.supportsAsyncGPUReadback)throw new PlatformNotSupportedException("GPU-completion-unavailable");asynchronous=true;lease=owner.Ledger.Reserve(new ResourceCost(64,128,4,1));try{sentinel=new ComputeBuffer(1,4,ComputeBufferType.Raw);lease.Commit();sentinel.SetData(new uint[1]);AsyncGPUReadback.Request(sentinel,request=>{error=request.hasError;done=true;});}catch{sentinel?.Dispose();lease.Dispose();throw;}}
  public bool Passed{get{if(disposed)throw new ObjectDisposedException(nameof(NativeGpuCompletion));if(!asynchronous)return fence.passed;if(error)throw new InvalidOperationException("GPU-completion-readback-failed");return done;}}
  public void Dispose(){if(disposed)return;if(!Passed)throw new InvalidOperationException("GPU-completion-not-finished");sentinel?.Dispose();lease?.Dispose();disposed=true;}
 }
}

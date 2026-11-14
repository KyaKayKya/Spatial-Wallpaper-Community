using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Security;
using SpatialWallpaper.Supervision.Preview;
using UnityEngine;

namespace SpatialWallpaper.Content {
 // One private input registry Host per original manager/DesktopRuntime process.
 // No Reserve/Start command, Worker, window, Tracker, tray or desktop ownership.
 public static class NativeBrokerLease {
  sealed class Launch {
   internal string DataRoot,RuntimeRoot,Workspace,Endpoint;
   internal IDictionary<string,string> Identity;
   internal readonly CancellationTokenSource Cancellation=new CancellationTokenSource();
   internal readonly TaskCompletionSource<string> Ready=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
   internal PreviewHostLease Host;
   internal Task Closing;
  }
  static readonly object gate=new object();static Launch active;
  public static Task<string> EnsureAsync(string dataRoot) {
   string data=Path.GetFullPath(dataRoot);Launch launch;
   lock(gate){
    if(active!=null){if(active.Closing!=null)throw new InvalidOperationException("native-broker-close-pending");if(!string.Equals(active.DataRoot,data,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("native-broker-data-root-changed");return active.Ready.Task;}
    // Unity/resource access occurs on the caller's main thread before awaiting.
    string runtime=ContentArguments.Argument("--sdk-runtime")??Path.GetFullPath(Path.Combine(Application.dataPath,"../sdk-runtime"));
    var rules=RuleCatalog.Current.Snapshot;var identity=new[]{"sdk","engine","pipeline","vm","backend"}.ToDictionary(key=>key,key=>(string)rules[key]);identity["rules_sha256"]=RuleCatalog.Current.Digest;identity["policy_id"]=RuntimeLimits.Id;
    launch=new Launch{DataRoot=data,RuntimeRoot=Path.GetFullPath(runtime),Identity=identity};active=launch;
   }
   _ = Start(launch);return launch.Ready.Task;
  }
  static async Task Start(Launch launch) {
   try{
    var token=launch.Cancellation.Token;
    await Task.Run(()=>TrustedRuntimeDistribution.Verify(launch.RuntimeRoot,launch.Identity,Path.Combine(launch.RuntimeRoot,"SpatialWallpaper.Preview.exe"),token),token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
    string parent=Path.Combine(launch.DataRoot,"native-input","launches");NoLinks(parent);Directory.CreateDirectory(parent);RuntimeDirectory.RestrictToCurrentUser(parent);
    launch.Workspace=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(launch.Workspace);RuntimeDirectory.RestrictToCurrentUser(launch.Workspace);
    string backend=Path.Combine(launch.Workspace,"backend"),attempts=Path.Combine(launch.Workspace,"attempts");Directory.CreateDirectory(attempts);launch.Endpoint=Path.Combine(backend,"endpoint.json");
    var options=new JObject{{"image",Path.Combine(launch.RuntimeRoot,"worker","player")},{"inventory",Path.Combine(launch.RuntimeRoot,"worker","report.json")},{"validator",Path.Combine(launch.RuntimeRoot,"validator")},{"state",backend},{"attempts",attempts},{"width",64},{"height",64}};
    var host=new PreviewHostLease(Path.Combine(launch.RuntimeRoot,"supervisor","Supervisor.exe"),Path.Combine(launch.Workspace,"host-config.json"),options);lock(gate)launch.Host=host;
    token.ThrowIfCancellationRequested();await host.StartAsync().ConfigureAwait(false);token.ThrowIfCancellationRequested();
    launch.Ready.TrySetResult(launch.Endpoint);
   }catch(OperationCanceledException){launch.Ready.TrySetCanceled();}
   catch(Exception error){launch.Ready.TrySetException(error);}
  }
  static void NoLinks(string directory){for(var current=new DirectoryInfo(directory);current!=null;current=current.Parent)if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("native-broker-workspace-link");}
  public static Task CloseAsync() {
   Launch launch;TaskCompletionSource<bool> completion;
   lock(gate){
    launch=active;if(launch==null)return Task.CompletedTask;
    if(launch.Closing!=null&&!launch.Closing.IsFaulted)return launch.Closing;
    launch.Cancellation.Cancel();completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);launch.Closing=completion.Task;
   }
   _ = Close(launch,completion);return completion.Task;
  }
  static async Task Close(Launch launch,TaskCompletionSource<bool> completion) {
   try{
    // If the private Host already exists, cancel its startup immediately.
    // If it is published later, Start sees our token and Close still owns it.
    PreviewHostLease early;lock(gate)early=launch.Host;Task stopping=early?.StopAsync();
    try{await launch.Ready.Task.ConfigureAwait(false);}catch(Exception){/* Start failure is returned to EnsureAsync callers; shutdown must still reclaim its retained Host. */}
    var host=launch.Host;if(host!=null){await (stopping??host.StopAsync()).ConfigureAwait(false);if(!host.ResourcesReleased)throw new InvalidOperationException("native-broker-cleanup-unproven");host.Dispose();}
    launch.Cancellation.Dispose();lock(gate){if(ReferenceEquals(active,launch))active=null;}
    // Keep the small owned launch directory/exit trace for diagnostics. It is
    // never reused as another process's state or as an installation save path.
    completion.TrySetResult(true);
   }catch(Exception error){completion.TrySetException(error);}
  }
 }
}

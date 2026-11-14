// Pure coordination checks. Fakes deliberately do not create OS input hooks,
// worker processes or native windows; actual Host cleanup remains its contract.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision.Preview;

namespace UnityEngine {public static class Application{public static string dataPath=Path.GetTempPath();}}
namespace SpatialTracking {public static class RuntimeDirectory{public static void RestrictToCurrentUser(string p){}}}
namespace SpatialWallpaper.Content {public static class ContentArguments{public static string Argument(string key)=>null;}}
namespace SpatialWallpaper.Sdk.Security {
 public sealed class RuleCatalog {
  public static readonly RuleCatalog Current=new RuleCatalog();public string Digest=>"rules";
  public JObject Snapshot=>new JObject{["sdk"]="0.4.0-preview.1",["engine"]="unity",["pipeline"]="urp",["vm"]="cilbox",["backend"]="native"};
 }
}
namespace SpatialWallpaper.Sdk.Hosting {
 public static class RuntimeLimits{public const string Id="policy";}
 public static class TrustedRuntimeDistribution {
  public static ManualResetEventSlim Continue=new ManualResetEventSlim(false),Entered=new ManualResetEventSlim(false);
  public static JObject Verify(string root,IDictionary<string,string> identity,string exe,CancellationToken cancel){Entered.Set();Continue.Wait(cancel);return new JObject();}
 }
}
namespace SpatialWallpaper.Supervision.Preview {
 public sealed class PreviewHostLease:IDisposable {
  public static bool AutoStart;public static int Created,Started,Stopped,Disposed;
  readonly TaskCompletionSource<bool> start=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);bool stopped;
  public PreviewHostLease(string exe,string config,JObject options){Interlocked.Increment(ref Created);if(options["audio"]!=null||options["llm"]!=null)throw new Exception("unrequested broker");}
  public Task StartAsync(){Interlocked.Increment(ref Started);if(AutoStart)start.TrySetResult(true);return start.Task;}
  public Task StopAsync(){if(!stopped){stopped=true;Interlocked.Increment(ref Stopped);}start.TrySetCanceled();return Task.CompletedTask;}
  public bool ResourcesReleased=>stopped;
  public void Dispose(){if(!stopped)throw new Exception("disposed live Host");Interlocked.Increment(ref Disposed);}
 }
}
static class Tests {
 static int checks;
 static void Check(bool b,string message){if(!b)throw new Exception(message);checks++;}
 static async Task Fails(Task task,string message){try{await task;throw new Exception("unexpected success: "+message);}catch(OperationCanceledException){checks++;}}
 static async Task Until(Func<bool> ready){for(int i=0;i<200&&!ready();i++)await Task.Delay(5);Check(ready(),"coordination deadline");}
 static async Task Main(){
  string data=Path.Combine(Path.GetTempPath(),"native-broker-tests-"+Guid.NewGuid().ToString("N"));
  try{
   // Close while distribution verification is pending cancels it and owns the
   // published launch even though no private Host has been constructed yet.
   var first=NativeBrokerLease.EnsureAsync(data);var same=NativeBrokerLease.EnsureAsync(data);Check(ReferenceEquals(first,same),"concurrent Ensure shares startup");
   Check(TrustedRuntimeDistribution.Entered.Wait(1000),"verification entered");await NativeBrokerLease.CloseAsync();await Fails(first,"cancel verifying");Check(PreviewHostLease.Created==0,"no Host after canceled verification");
   await NativeBrokerLease.CloseAsync();Check(PreviewHostLease.Disposed==0,"empty close idempotent");
   // Close racing a constructed/starting Host must stop that exact lease and
   // must not expose its endpoint to callers after cancellation.
   TrustedRuntimeDistribution.Continue.Set();var starting=NativeBrokerLease.EnsureAsync(data);await Until(()=>PreviewHostLease.Started==1);
   await NativeBrokerLease.CloseAsync();await Fails(starting,"cancel host startup");Check(PreviewHostLease.Stopped==1&&PreviewHostLease.Disposed==1,"starting Host reclaimed exactly once");
   // A completed Host is reused by this process and cannot silently rebind to
   // another data root. Explicit close permits a clean subsequent generation.
   PreviewHostLease.AutoStart=true;var endpoint=await NativeBrokerLease.EnsureAsync(data);Check(endpoint.StartsWith(data,StringComparison.Ordinal)&&endpoint.EndsWith("backend"+Path.DirectorySeparatorChar+"endpoint.json",StringComparison.Ordinal),"owned endpoint path");
   Check(await NativeBrokerLease.EnsureAsync(data)==endpoint,"ready lease reused");
   try{_ = NativeBrokerLease.EnsureAsync(data+"-other");throw new Exception("root rebound");}catch(InvalidOperationException){checks++;}
   await NativeBrokerLease.CloseAsync();Check(PreviewHostLease.Stopped==2&&PreviewHostLease.Disposed==2,"ready Host reclaimed exactly once");
   var fresh=await NativeBrokerLease.EnsureAsync(data);Check(fresh!=endpoint,"new generation has fresh workspace");await NativeBrokerLease.CloseAsync();Check(PreviewHostLease.Created==3&&PreviewHostLease.Disposed==3,"all owned leases reclaimed");
  }finally{await NativeBrokerLease.CloseAsync();if(Directory.Exists(data))Directory.Delete(data,true);}
  Console.WriteLine("Native broker lifecycle checks passed: "+checks);
 }
}

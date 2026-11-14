using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Supervision {
 // Invoked only after the manager channel authenticates the caller. The host
 // identity binds all responses, including absence, to one backend lifetime.
 public sealed class OwnedSessionProtocol {
  readonly OwnedSessionRegistry registry;readonly Guid host;
  public OwnedSessionProtocol(OwnedSessionRegistry registry,Guid host){if(registry==null||host==Guid.Empty)throw new ArgumentException();this.registry=registry;this.host=host;}
  public static bool Handles(string operation)=>operation=="StartSession"||operation=="SessionStatus"||operation=="StopSession"||operation=="RetrySessionCleanup"||operation=="ForgetSession";
  // Keep even fully JSON-escaped diagnostics within the manager reply budget.
  // The owner retains the original exception; control replies are summaries.
  static JToken Error(Exception error){if(error==null)return JValue.CreateNull();string text=error.GetBaseException().Message;return text.Length>1024?text.Substring(0,1024):text;}
  public JObject Status(Guid token){var value=registry.Status(token);return new JObject{
   {"ok",true},{"host",host.ToString("N")},{"token",token.ToString("N")},{"state",value.State},{"resources_released",value.ResourcesReleased},
   {"instance",value.Request==null?JValue.CreateNull():(JToken)value.Request.Instance.ToString("N")},
   {"generation",value.Request==null?JValue.CreateNull():(JToken)value.Request.Generation},
   {"attempt",value.Identity==null?JValue.CreateNull():(JToken)value.Identity.Attempt.ToString("N")},
   {"primary_error",Error(value.PrimaryError)},{"cleanup_error",Error(value.CleanupError)}};}
  public JObject Dispatch(string operation,JObject payload){
   if(!Handles(operation)||payload==null||!payload.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"host","token"})||payload["host"].Type!=JTokenType.String||payload["token"].Type!=JTokenType.String)throw new InvalidDataException("session-command-fields");
   if((string)payload["host"]!=host.ToString("N"))throw new InvalidDataException("session-host-mismatch");
   Guid token;if(!Guid.TryParseExact((string)payload["token"],"N",out token)||token==Guid.Empty)throw new InvalidDataException("session-token");
   switch(operation){
    case "StartSession":registry.Start(token);break;
    case "StopSession":registry.Stop(token);break;
    case "RetrySessionCleanup":
     // Do not hold a manager connection while cleanup runs. The stable entry
     // remains queryable and cannot be forgotten until release is proven.
     var retry=registry.RetryCleanupAsync(token);retry.ContinueWith(t=>{var observed=t.Exception;},System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);break;
    case "ForgetSession":registry.Forget(token);break;
   }
   return Status(token);
  }
 }
}

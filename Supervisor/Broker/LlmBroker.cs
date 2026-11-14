using System;using System.IO;using System.Net;using System.Text;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Supervision.Broker {
 public enum LlmFailure {None,Busy,Closed,Cancelled,Timeout,Unavailable,Invalid,TooLarge,Transport,Protocol,Redirect,Denied}
 public sealed class LlmResult {
  public LlmFailure Failure{get;}public string Text{get;}
  internal LlmResult(LlmFailure failure,string text=null){Failure=failure;Text=text;}
 }
 // Constructed only from platform settings. No Worker request can select these values.
 public sealed class LlmConfiguration {
  internal readonly Uri Endpoint;internal readonly string Model,Credential;internal readonly int TimeoutMs;
  public LlmConfiguration(Uri endpoint,string model,string credential,int timeoutMs=15000){
   if(endpoint==null||!endpoint.IsAbsoluteUri||endpoint.Scheme!="https"&&endpoint.Scheme!="http"||endpoint.UserInfo.Length!=0||endpoint.Fragment.Length!=0||endpoint.AbsoluteUri.Length>2048||string.IsNullOrEmpty(model)||model.Length>128||credential==null||credential.Length>4096||credential.IndexOfAny(new[]{'\r','\n','\0'})>=0||timeoutMs<100||timeoutMs>60000)throw new ArgumentException("LLM-platform-configuration");
   Endpoint=endpoint;Model=model;Credential=credential;TimeoutMs=timeoutMs;
  }
  public static LlmConfiguration Parse(JToken token){
   if(token==null||token.Type==JTokenType.Null)return null;
   var value=token as JObject;if(value==null||value.Count!=3||value["endpoint"]?.Type!=JTokenType.String||value["model"]?.Type!=JTokenType.String||value["credential"]?.Type!=JTokenType.String||!Uri.TryCreate((string)value["endpoint"],UriKind.Absolute,out var endpoint))throw new ArgumentException("LLM-platform-configuration");
   return new LlmConfiguration(endpoint,(string)value["model"],(string)value["credential"]);
  }
 }
 // Eight credits include queued/running/aborting work. Cancellation does not release credit until I/O exits.
 public sealed class LlmBroker:IDisposable {
  public const int Capacity=8,MaxPromptChars=4096,MaxReplyChars=4096,MaxReplyBytes=32768;
  sealed class Work {
   internal BrokerSession Session;internal int Id,Failure;internal bool Terminal;internal HttpWebRequest Request;internal Timer Timer;internal readonly Stopwatch Elapsed=Stopwatch.StartNew();
   // Called under the broker gate: cancellation and result publication share one terminal decision.
   internal void Fail(LlmFailure reason){if(!Terminal)Interlocked.CompareExchange(ref Failure,(int)reason,0);}
  }
  readonly object gate=new object();readonly List<Work> work=new List<Work>(Capacity);readonly LlmConfiguration configuration;bool closed;
  readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task Completion=>completion.Task;
#if SDK_SUPERVISOR_TESTS
  public static Action BeforeResult,AfterResult;
#endif
  public LlmBroker(LlmConfiguration settings){configuration=settings;}
  static Task<LlmResult> Failed(LlmFailure failure)=>Task.FromResult(new LlmResult(failure));
  static LlmFailure Permission(BrokerSession session){try{session.Demand("llm-proxy");return LlmFailure.None;}catch(UnauthorizedAccessException){return LlmFailure.Denied;}catch(InvalidOperationException){return LlmFailure.Closed;}}
  public Task<LlmResult> Submit(BrokerSession session,int id,string prompt){
   if(session==null||id<1||prompt==null||prompt.Length>MaxPromptChars)return Failed(LlmFailure.Invalid);
   try{new UTF8Encoding(false,true).GetByteCount(prompt);}catch(EncoderFallbackException){return Failed(LlmFailure.Invalid);}
   var permission=Permission(session);if(permission!=LlmFailure.None)return Failed(permission);
   lock(gate){
    if(closed)return Failed(LlmFailure.Closed);if(configuration==null)return Failed(LlmFailure.Unavailable);if(work.Count>=Capacity)return Failed(LlmFailure.Busy);
    if(work.Exists(item=>ReferenceEquals(item.Session,session)&&item.Id==id))return Failed(LlmFailure.Invalid);
    var item=new Work{Session=session,Id=id};work.Add(item);
    item.Timer=new Timer(_=>{
     lock(gate){if(item.Terminal)return;var access=Permission(item.Session);if(access!=LlmFailure.None)item.Fail(access);if(item.Elapsed.ElapsedMilliseconds>=configuration.TimeoutMs)item.Fail(LlmFailure.Timeout);}
     if(Volatile.Read(ref item.Failure)!=0){try{Volatile.Read(ref item.Request)?.Abort();}catch{}}
    },null,0,20);
    return Task.Run(()=>Run(item,prompt));
   }
  }
  LlmResult Run(Work item,string prompt){
   try{
    var result=RunTransport(item,prompt);
#if SDK_SUPERVISOR_TESTS
    BeforeResult?.Invoke();
#endif
    lock(gate){
     var permission=Permission(item.Session);if(permission!=LlmFailure.None)item.Fail(permission);
     if(item.Elapsed.ElapsedMilliseconds>=configuration.TimeoutMs)item.Fail(LlmFailure.Timeout);
     if(item.Failure!=0)result=new LlmResult((LlmFailure)item.Failure);
     item.Terminal=true;
    }
#if SDK_SUPERVISOR_TESTS
    AfterResult?.Invoke();
#endif
    return result;
   }finally{item.Timer.Dispose();lock(gate){work.Remove(item);if(closed&&work.Count==0)completion.TrySetResult(true);}}
  }
  LlmResult RunTransport(Work item,string prompt){
   try{
    if(Volatile.Read(ref item.Failure)!=0)return new LlmResult((LlmFailure)item.Failure);
    byte[] body=new UTF8Encoding(false,true).GetBytes(new JObject{{"model",configuration.Model},{"messages",new JArray(new JObject{{"role","user"},{"content",prompt}})},{"stream",false},{"max_tokens",1024}}.ToString(Formatting.None));
    var request=(HttpWebRequest)WebRequest.Create(configuration.Endpoint);request.Method="POST";request.AllowAutoRedirect=false;request.UseDefaultCredentials=false;request.Credentials=null;request.Proxy=null;request.CookieContainer=null;request.AutomaticDecompression=DecompressionMethods.None;request.MaximumResponseHeadersLength=16;request.Timeout=configuration.TimeoutMs;request.ReadWriteTimeout=configuration.TimeoutMs;request.ContentType="application/json";request.Accept="application/json";request.ContentLength=body.Length;
    if(configuration.Credential.Length>0)request.Headers[HttpRequestHeader.Authorization]="Bearer "+configuration.Credential;Volatile.Write(ref item.Request,request);
    if(Volatile.Read(ref item.Failure)!=0){request.Abort();return new LlmResult((LlmFailure)item.Failure);}
    using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
    using(var response=(HttpWebResponse)request.GetResponse()){
     int status=(int)response.StatusCode;if(status>=300&&status<400)return new LlmResult(LlmFailure.Redirect);if(status<200||status>=300)return new LlmResult(LlmFailure.Transport);
     if(response.ContentLength>MaxReplyBytes)return new LlmResult(LlmFailure.TooLarge);if(!string.IsNullOrEmpty(response.ContentEncoding))return new LlmResult(LlmFailure.Protocol);
     byte[] bytes=new byte[MaxReplyBytes+1];int count=0;using(var stream=response.GetResponseStream()){while(count<bytes.Length){int read=stream.Read(bytes,count,bytes.Length-count);if(read==0)break;count+=read;}}
     if(count>MaxReplyBytes)return new LlmResult(LlmFailure.TooLarge);if(response.ContentLength>=0&&count!=response.ContentLength)return new LlmResult(LlmFailure.Transport);string json=new UTF8Encoding(false,true).GetString(bytes,0,count);JObject data;
     using(var input=new StringReader(json))using(var reader=new JsonTextReader(input){MaxDepth=8,DateParseHandling=DateParseHandling.None}){int tokens=0;while(reader.Read())if(++tokens>256)return new LlmResult(LlmFailure.TooLarge);}
     using(var input=new StringReader(json))using(var reader=new JsonTextReader(input){MaxDepth=8,DateParseHandling=DateParseHandling.None}){data=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())return new LlmResult(LlmFailure.Protocol);}
     var value=data["choices"]?[0]?["message"]?["content"];if(value==null||value.Type!=JTokenType.String)return new LlmResult(LlmFailure.Protocol);string text=(string)value;if(text.Length>MaxReplyChars)return new LlmResult(LlmFailure.TooLarge);
     var permission=Permission(item.Session);if(permission!=LlmFailure.None)return new LlmResult(permission);if(Volatile.Read(ref item.Failure)!=0)return new LlmResult((LlmFailure)item.Failure);
      return new LlmResult(LlmFailure.None,text);
    }
   }catch(WebException error){error.Response?.Dispose();return new LlmResult(Volatile.Read(ref item.Failure)!=0?(LlmFailure)item.Failure:error.Status==WebExceptionStatus.Timeout?LlmFailure.Timeout:LlmFailure.Transport);}
   catch(Exception error)when(error is IOException||error is JsonException||error is ArgumentException||error is InvalidOperationException||error is System.Security.SecurityException){return new LlmResult(Volatile.Read(ref item.Failure)!=0?(LlmFailure)item.Failure:LlmFailure.Protocol);}
  }
  public bool Cancel(BrokerSession session,int id){lock(gate){var item=work.Find(candidate=>ReferenceEquals(candidate.Session,session)&&candidate.Id==id);if(item==null||item.Terminal)return false;item.Fail(LlmFailure.Cancelled);return item.Failure==(int)LlmFailure.Cancelled;}}
  public void CancelSession(BrokerSession session){lock(gate)foreach(var item in work)if(ReferenceEquals(item.Session,session))item.Fail(LlmFailure.Closed);}
  public void Dispose(){lock(gate){closed=true;foreach(var item in work)item.Fail(LlmFailure.Closed);if(work.Count==0)completion.TrySetResult(true);}}
 }
}

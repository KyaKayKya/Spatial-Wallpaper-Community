using System;using System.IO;using System.Net;using System.Net.Sockets;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;
static class LlmChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static BrokerSession Session(FaultJournal journal)=>new BrokerSession(journal.Begin(Guid.NewGuid(),new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"llm-proxy"});
 static int Main(string[] args){try{
   var allocation=new TcpListener(IPAddress.Loopback,0);allocation.Start();int port=((IPEndPoint)allocation.LocalEndpoint).Port;allocation.Stop();string origin="http://127.0.0.1:"+port;int redirected=0,received=0;string seen=null;var handlers=new List<Task>();
  using(var listener=new HttpListener())using(var journal=new FaultJournal(Path.Combine(args[0],"journal"))){
    listener.Prefixes.Add(origin+"/");listener.Start();var accepting=Task.Run(async()=>{while(listener.IsListening){HttpListenerContext context;try{context=await listener.GetContextAsync();}catch{break;}var handling=Task.Run(()=>{try{
    using(var reader=new StreamReader(context.Request.InputStream))seen=reader.ReadToEnd();Interlocked.Increment(ref received);string path=context.Request.Url.AbsolutePath;byte[] bytes;
    if(path=="/redirect"){context.Response.StatusCode=307;context.Response.RedirectLocation=origin+"/secret-sink";bytes=new byte[0];}
    else if(path=="/secret-sink"){Interlocked.Increment(ref redirected);bytes=new byte[0];}
    else{Need(context.Request.Headers["Authorization"]=="Bearer test-secret-never-in-result","missing-platform-auth");if(path=="/slow")Thread.Sleep(2500);string content=path=="/large"?new string('x',40000):"{\"choices\":[{\"message\":{\"content\":\"reply-as-data\"}}]}";bytes=Encoding.UTF8.GetBytes(content);}
    context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;context.Response.OutputStream.Write(bytes,0,bytes.Length);
    }catch{}finally{try{context.Response.Close();}catch{}}});lock(handlers)handlers.Add(handling);}});
   foreach(string path in new[]{"/ok","/redirect","/large"})using(var broker=new LlmBroker(new LlmConfiguration(new Uri(origin+path),"fixed-model","test-secret-never-in-result",1000))){
    var result=broker.Submit(Session(journal),1,"author prompt").GetAwaiter().GetResult();Need(result.Failure==(path=="/ok"?LlmFailure.None:path=="/redirect"?LlmFailure.Redirect:LlmFailure.TooLarge),"LLM-result:"+path+":"+result.Failure);
    if(path=="/ok"){Need(result.Text=="reply-as-data","LLM-text");Need(seen.Contains("fixed-model")&&seen.Contains("author prompt")&&!seen.Contains("test-secret"),"LLM-request-shape");}
   }
    Need(redirected==0,"credentials-followed-redirect");
    LlmLifecycleChecks.Run(origin,journal);
    LlmTransportChecks.Run(journal);
    LlmEndpointChecks.Run(origin,args[0],journal);
   using(var broker=new LlmBroker(new LlmConfiguration(new Uri(origin+"/slow"),"fixed-model","test-secret-never-in-result",300))){
    var session=Session(journal);var pending=new List<Task<LlmResult>>();for(int i=1;i<=8;i++)pending.Add(broker.Submit(session,i,"bounded"));
    Need(broker.Submit(session,9,"overflow").Result.Failure==LlmFailure.Busy,"unbounded-LLM-capacity");
    Need(broker.Cancel(session,1),"cancel-owned-request");Need(!broker.Cancel(Session(journal),2),"cross-session-cancel");
    Need(pending[0].Result.Failure==LlmFailure.Cancelled,"cancel-result");for(int i=1;i<8;i++)Need(pending[i].Result.Failure==LlmFailure.Timeout,"deadline-result");
     pending.Clear();for(int i=10;i<18;i++)pending.Add(broker.Submit(session,i,"capacity recovered"));Need(broker.Submit(session,18,"overflow again").Result.Failure==LlmFailure.Busy,"capacity-remains-eight-after-timeout");
     for(int i=10;i<18;i++)Need(broker.Cancel(session,i),"recovered-credit-cancellable");for(int i=0;i<8;i++)Need(pending[i].Result.Failure==LlmFailure.Cancelled,"all-eight-credits-recovered");
    var closing=Session(journal);var old=broker.Submit(closing,1,"late");closing.Close();Need(old.Result.Failure==LlmFailure.Closed,"old-session-reply");
    Need(broker.Submit(Session(journal),1,new string('x',4097)).Result.Failure==LlmFailure.Invalid,"prompt-bound");
   }
    listener.Stop();Need(accepting.Wait(1000),"HTTP-acceptor-exited");Task[] remaining;lock(handlers)remaining=handlers.ToArray();Need(Task.WaitAll(remaining,5000),"HTTP-handlers-exited");
  }
  Console.WriteLine("PASS real HTTP prompt/model/auth, redirect refusal, bounded response, capacity, cancellation, timeout and old session rejection");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

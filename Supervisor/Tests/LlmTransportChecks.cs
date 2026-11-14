using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Broker;

static class LlmTransportChecks {
 const string Good="{\"choices\":[{\"message\":{\"content\":\"fixed reply https://example.invalid/ is data\"}}]}";
 static void Need(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void ReadRequest(NetworkStream stream){
  using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
   string line=reader.ReadLine();if(line==null)throw new IOException("request-missing");int length=0,total=line.Length;
   while((line=reader.ReadLine())!=null&&line.Length>0){total+=line.Length;if(total>16384)throw new IOException("headers-too-large");if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Substring(15).Trim());}
   if(length<1||length>32768)throw new IOException("request-length");char[] body=new char[length];int offset=0;while(offset<length){int n=reader.Read(body,offset,length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
  }
 }
 public static void Run(FaultJournal journal){
  foreach(string mode in new[]{"partial-length","headers-only","malformed","duplicate","wrong-shape","encoded","unknown-length-large"}){
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
   var serving=Task.Run(()=>{for(int attempt=0;attempt<2;attempt++)using(var client=listener.AcceptTcpClient()){
    client.ReceiveTimeout=3000;client.SendTimeout=3000;using(var stream=client.GetStream()){
     ReadRequest(stream);string body=Good,extra="";bool omitLength=false;int declared=-1;
     if(attempt==0){switch(mode){
      case "partial-length":declared=Encoding.UTF8.GetByteCount(body)+100;break;
      case "headers-only":body="";declared=100;break;
      case "malformed":body="{";break;
      case "duplicate":body="{\"choices\":[],\"choices\":[]}";break;
      case "wrong-shape":body="{\"choices\":true}";break;
      case "encoded":extra="Content-Encoding: gzip\r\n";break;
      case "unknown-length-large":body=new string('x',LlmBroker.MaxReplyBytes+1);omitLength=true;break;
     }}
     byte[] bytes=Encoding.UTF8.GetBytes(body);string header="HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: application/json\r\n"+extra+(omitLength?"":"Content-Length: "+(declared<0?bytes.Length:declared)+"\r\n")+"\r\n";
     byte[] prefix=Encoding.ASCII.GetBytes(header);stream.Write(prefix,0,prefix.Length);stream.Write(bytes,0,bytes.Length);stream.Flush();
    }
   }});
   var broker=new LlmBroker(new LlmConfiguration(new Uri("http://127.0.0.1:"+port+"/"),"fixed-model","",1500));
   var session=new BrokerSession(journal.Begin(Guid.NewGuid(),new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"llm-proxy"});
   try{
    var bad=broker.Submit(session,1,"fault response");Need(bad.Wait(3000),"bounded-response-"+mode);
    var expected=mode=="partial-length"||mode=="headers-only"?LlmFailure.Transport:mode=="unknown-length-large"?LlmFailure.TooLarge:LlmFailure.Protocol;
    Need(bad.Result.Failure==expected&&bad.Result.Text==null,"reject-response-"+mode+":"+bad.Result.Failure);
    var good=broker.Submit(session,2,"next request");Need(good.Wait(3000)&&good.Result.Failure==LlmFailure.None&&good.Result.Text=="fixed reply https://example.invalid/ is data","recover-after-"+mode);
   }finally{broker.Dispose();Need(broker.Completion.Wait(3000),"io-reclaimed-"+mode);listener.Stop();try{Need(serving.Wait(3000),"fixture-reclaimed-"+mode);}catch(AggregateException e){if(!(e.GetBaseException() is SocketException))throw;}}
  }
 }
}

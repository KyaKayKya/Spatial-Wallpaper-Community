using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;

static class Tests {
 static int checks;static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
 static readonly byte[] Package=Encoding.UTF8.GetBytes("not a real archive: importer must still validate");
 static readonly RSA Key=RSA.Create(2048);
 static MarketTrust Trust(){var p=Key.ExportParameters(false);return new MarketTrust{Repository="KyaKayKya/Spatial-Wallpaper-Community",CatalogUrl="https://github.com/KyaKayKya/Spatial-Wallpaper-Community/releases/download/market-catalog/published.json",SignatureUrl="https://github.com/KyaKayKya/Spatial-Wallpaper-Community/releases/download/market-catalog/published.sig.json",KeyId="test",Modulus=Convert.ToBase64String(p.Modulus),Exponent=Convert.ToBase64String(p.Exponent)};}
 static string Hash(byte[] b){using(var sha=SHA256.Create())return MarketCatalog.Hex(sha.ComputeHash(b));}
 static string Time(DateTimeOffset t)=>t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
 static JObject Snapshot(){return new JObject{["schemaVersion"]=2,["kind"]="published",["generation"]=2,["generatedAt"]=Time(Now.AddMinutes(-2)),["expiresAt"]=Time(Now.AddDays(7)),["revocations"]=new JObject{["works"]=new JArray(),["builds"]=new JArray()},["entries"]=new JArray(new JObject{
  ["key"]="123:work",["repositoryId"]=123,["ownerId"]=456,["repository"]="owner/repo",["workId"]="work",["title"]="Test title",["sdk"]="0.4.0-preview.1",["capabilities"]=new JArray("sound-playback"),["attribution"]=new JArray(new JObject{["name"]="Author",["license"]="CC0-1.0",["githubUserId"]=456}),["status"]="published",["firstListedAt"]=Time(Now.AddDays(-10)),
  ["build"]=new JObject{["version"]="1.0.0",["releaseId"]=12,["tag"]="v1",["identity"]=new string('a',64),["assets"]=new JArray(new JObject{["id"]=77,["name"]="work.spatial",["size"]=Package.Length,["sha256"]=Hash(Package),["url"]="https://github.com/owner/repo/releases/download/v1/work.spatial"})},
  ["admission"]=new JObject{["schemaVersion"]=5,["sdkVersion"]="0.4.0-preview.1",["sceneCommandAbi"]=1,["policy"]="native-admission-v1",["buildIdentity"]=new string('a',64),["packageAssetId"]=77},["comments"]=new JObject{["kind"]="issues",["number"]=8,["url"]="https://github.com/KyaKayKya/Spatial-Wallpaper-Community/issues/8"},["popularity"]=new JObject{["downloads7d"]=8}
 })};}
 static byte[] Bytes(JObject o)=>Encoding.UTF8.GetBytes(o.ToString(Formatting.None));
 static string Sign(byte[] bytes){var prefix=Encoding.UTF8.GetBytes("spatial-market-published-v2\0");var message=new byte[prefix.Length+bytes.Length];Buffer.BlockCopy(prefix,0,message,0,prefix.Length);Buffer.BlockCopy(bytes,0,message,prefix.Length,bytes.Length);return new JObject{["algorithm"]="RSA-PKCS1-SHA256",["keyId"]="test",["sha256"]=Hash(bytes),["signature"]=Convert.ToBase64String(Key.SignData(message,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))}.ToString(Formatting.None);}
 static MarketCatalog Verify(JObject o,long watermark=0,string digest=null,bool offline=false){var b=Bytes(o);return MarketCatalog.Verify(b,Sign(b),Trust(),watermark,digest,Now,offline);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static void Reject(Action action,string name){try{action();}catch(Exception){checks++;return;}throw new Exception("accepted "+name);}
 static async Task RejectAsync(Func<Task> action,string name){try{await action();}catch(Exception){checks++;return;}throw new Exception("accepted "+name);}
 sealed class Transport:HttpMessageHandler {
  public byte[] Catalog=Bytes(Snapshot()),Body=Package;public int Status=200;public string Redirect;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){
   var response=new HttpResponseMessage((HttpStatusCode)Status);if(Redirect!=null){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new Uri(Redirect);}
   else response.Content=new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("published.json")?Catalog:request.RequestUri.AbsolutePath.EndsWith("published.sig.json")?Encoding.UTF8.GetBytes(Sign(Catalog)):Body);
   return Task.FromResult(response);
  }
 }
 static async Task Main(){
  var o=Snapshot();var catalog=Verify(o);Check(catalog.Entries.Length==1&&catalog.Installable(catalog.Entries[0],Now),"valid published index");
  Check(catalog.Entries[0].Feedback=="https://github.com/KyaKayKya/Spatial-Wallpaper-Community/issues/8","public community issue mapping");
  Check(catalog.Search("author","声音",true).Length==1,"search author + capability filter");Check(catalog.Search("unknown","全部",true).Length==0,"search empty result");
  var b=Bytes(o);b[0]^=1;Reject(()=>MarketCatalog.Verify(b,Sign(Bytes(o)),Trust(),0,null,Now),"digest tampering");
  var wrong=Trust();wrong.KeyId="wrong";Reject(()=>MarketCatalog.Verify(Bytes(o),Sign(Bytes(o)),wrong,0,null,Now),"untrusted key");
  Reject(()=>Verify(o,3),"rollback");Reject(()=>Verify(o,2,new string('b',64)),"same generation equivocation");
  var x=(JObject)o.DeepClone();x["kind"]="candidate";Reject(()=>Verify(x),"candidate not installable");
  x=(JObject)o.DeepClone();x["generatedAt"]=Time(Now.AddDays(1));Reject(()=>Verify(x),"future catalog");
  x=(JObject)o.DeepClone();x["generatedAt"]=Time(Now.AddDays(-3));x["expiresAt"]=Time(Now.AddDays(-1));Reject(()=>Verify(x),"expired online");var expired=Verify(x,0,null,true);Check(!expired.Installable(expired.Entries[0],Now),"expired offline browse only");
  x=(JObject)o.DeepClone();x["entries"][0]["admission"]["schemaVersion"]=4;Reject(()=>Verify(x),"schema4 cannot relabel");
  x=(JObject)o.DeepClone();x["entries"][0]["admission"]["buildIdentity"]=new string('b',64);Reject(()=>Verify(x),"admission identity mismatch");
  x=(JObject)o.DeepClone();x["entries"][0]["build"]["assets"][0]["url"]="https://evil.test/work.spatial";Reject(()=>Verify(x),"arbitrary source");
  x=(JObject)o.DeepClone();x["entries"][0]["build"]["assets"][0]["name"]="../work.spatial";Reject(()=>Verify(x),"path traversal");
  x=(JObject)o.DeepClone();x["entries"][0]["comments"]["url"]="https://github.com/evil/repo/issues/8";Reject(()=>Verify(x),"comment spoof");
  x=(JObject)o.DeepClone();((JArray)x["revocations"]["works"]).Add("123:work");var revoked=Verify(x);Check(!revoked.Installable(revoked.Entries[0],Now),"revocation");
  Reject(()=>MarketCatalog.ParseObject("{\"a\":1,\"a\":2}"),"duplicate JSON key");
  Reject(()=>MarketCatalog.BrowserUrl("file:///C:/Windows/System32/cmd.exe"),"file url");Reject(()=>MarketCatalog.BrowserUrl("https://github.com/owner/repo?command=test"),"query launch");Check(MarketCatalog.BrowserUrl("https://github.com/owner/repo")=="https://github.com/owner/repo","source URL");
  Reject(()=>MarketCatalog.OfficialRelease("https://github.com/KyaKayKya/Spatial-Wallpaper/releases/download/catalog/published.json",Trust().Repository),"private product repo is not community trust source");
  Reject(()=>MarketCatalog.PreviewUrl("https://127.0.0.1/private.png"),"preview SSRF");
  var missing=(JObject)o.DeepClone();((JObject)missing["entries"][0]).Remove("popularity");Check(!Verify(missing).Entries[0].Downloads7Days.HasValue,"unknown popularity not zero");
  string directory=Path.Combine(Path.GetTempPath(),"spatial-market-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  try{
   var transport=new Transport();using(var client=new GitHubMarketClient(Trust(),directory,transport)){
    await client.Refresh(CancellationToken.None);Check(client.Catalog.Generation==2,"online refresh");string target=Path.Combine(directory,"download.spatial");await client.Download(client.Catalog.Entries[0],target,CancellationToken.None);Check(File.ReadAllBytes(target).Length==Package.Length,"verified download");
    await RejectAsync(()=>client.Download(client.Catalog.Entries[0],target,CancellationToken.None),"exclusive staging");Check(File.Exists(target),"existing file protected");File.Delete(target);
    transport.Body=Encoding.UTF8.GetBytes(new string('x',Package.Length));await RejectAsync(()=>client.Download(client.Catalog.Entries[0],target,CancellationToken.None),"digest mismatch");Check(!File.Exists(target),"failed staging removed");
    transport.Status=429;await RejectAsync(()=>client.Refresh(CancellationToken.None),"rate limit");Check(client.Catalog.Generation==2&&client.Notice.Contains("限流"),"rate limit retains cache");
    transport.Status=200;transport.Redirect="https://evil.test/private";await RejectAsync(()=>client.Refresh(CancellationToken.None),"redirect rejection");
    transport.Redirect=null;var old=Snapshot();old["generation"]=1;transport.Catalog=Bytes(old);await RejectAsync(()=>client.Refresh(CancellationToken.None),"persistent watermark");
   }
   using(var cached=new GitHubMarketClient(Trust(),directory,new Transport()))Check(cached.Catalog.Generation==2,"offline persisted verified snapshot");
  }finally{Directory.Delete(directory,true);}
  Console.WriteLine("Market client checks passed: "+checks);
 }
}

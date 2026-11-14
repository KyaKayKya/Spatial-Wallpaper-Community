using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Content {
 public sealed class GitHubMarketClient:IDisposable {
  readonly MarketTrust trust;readonly string cachePath;readonly HttpClient http;long watermark;string priorDigest;
  public MarketCatalog Catalog{get;private set;}
  public string Repository=>trust.Repository;
  public string Notice{get;private set;}="尚未获取市场目录。";
  public GitHubMarketClient(MarketTrust trust,string directory,HttpMessageHandler transport=null) {
   this.trust=trust;Directory.CreateDirectory(directory);cachePath=Path.Combine(directory,"catalog-cache.json");
   http=transport==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false}):new HttpClient(transport);http.Timeout=TimeSpan.FromSeconds(45);http.DefaultRequestHeaders.UserAgent.ParseAdd("SpatialWallpaper/0.4.0-preview.1");
   if(File.Exists(cachePath)){
    var cache=MarketCatalog.ParseObject(File.ReadAllText(cachePath));watermark=(long)cache["generation"];priorDigest=(string)cache["sha256"];
    Catalog=MarketCatalog.Verify(Convert.FromBase64String((string)cache["catalog"]),(string)cache["signature"],trust,watermark,priorDigest,DateTimeOffset.UtcNow,true);
    Notice=Catalog.Fresh(DateTimeOffset.UtcNow)?"显示已验证缓存；刷新可检查更新。":"缓存已过期，可离线浏览；刷新前不能安装。";
   }
  }
  static void Redirect(Uri uri){MarketCatalog.Require(uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo==""&&(uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"),"下载重定向来源不受支持");}
  internal async Task<byte[]> Fetch(string url,long limit,CancellationToken cancel,bool preview=false) {
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){timeout.CancelAfter(TimeSpan.FromSeconds(45));return await FetchCore(url,limit,timeout.Token,preview);}
  }
  async Task<byte[]> FetchCore(string url,long limit,CancellationToken cancel,bool preview) {
   if(preview)MarketCatalog.PreviewUrl(url);else MarketCatalog.OfficialRelease(url,trust.Repository);
   Uri uri=new Uri(url);for(int redirects=0;redirects<4;redirects++){
    using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancel)){
     int code=(int)response.StatusCode;
     if(code==301||code==302||code==303||code==307||code==308){MarketCatalog.Require(response.Headers.Location!=null,"下载重定向缺少目标");uri=new Uri(uri,response.Headers.Location);Redirect(uri);continue;}
     if(code==403||code==429)throw new HttpRequestException("GitHub 限流或拒绝访问，请稍后重试。"+(response.Headers.RetryAfter==null?"":" 重试时间："+response.Headers.RetryAfter));
     if(!response.IsSuccessStatusCode)throw new HttpRequestException("GitHub 网络请求失败："+code);
     MarketCatalog.Require(!response.Content.Headers.ContentLength.HasValue||response.Content.Headers.ContentLength<=limit,"下载超过预算");
     using(var input=await response.Content.ReadAsStreamAsync())using(var output=new MemoryStream()){
      var buffer=new byte[65536];int n;while((n=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0){MarketCatalog.Require(output.Length+n<=limit,"下载超过预算");output.Write(buffer,0,n);}return output.ToArray();
     }
    }
   }throw new HttpRequestException("GitHub 下载重定向次数过多");
  }
  public Task<byte[]> Preview(string url,CancellationToken cancel)=>Fetch(url,2*1024*1024,cancel,true);
  public async Task Refresh(CancellationToken cancel) {
   try {
    var bytes=await Fetch(trust.CatalogUrl,MarketCatalog.MaxIndexBytes,cancel);var sig=Encoding.UTF8.GetString(await Fetch(trust.SignatureUrl,16384,cancel));
    var verified=MarketCatalog.Verify(bytes,sig,trust,watermark,priorDigest,DateTimeOffset.UtcNow);
    var next=new JObject{["generation"]=verified.Generation,["sha256"]=verified.Digest,["catalog"]=Convert.ToBase64String(bytes),["signature"]=sig};
    AtomicWrite(cachePath,next.ToString(Formatting.None));Catalog=verified;watermark=verified.Generation;priorDigest=verified.Digest;Notice="目录已验证 · "+verified.Entries.Length+" 件作品。";
   }catch(Exception e) when(e is IOException||e is HttpRequestException||e is CryptographicException||e is JsonException||e is TaskCanceledException||e is InvalidOperationException||e is FormatException) {
    Notice="刷新失败："+e.GetBaseException().Message+(Catalog==null?"":" 正在显示上次已验证缓存。");throw;
   }
  }
  public async Task Download(MarketEntry entry,string target,CancellationToken cancel) {
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){timeout.CancelAfter(TimeSpan.FromMinutes(10));await DownloadCore(entry,target,timeout.Token);}
  }
  async Task DownloadCore(MarketEntry entry,string target,CancellationToken cancel) {
   MarketCatalog.Require(Catalog!=null&&Catalog.Installable(entry,DateTimeOffset.UtcNow),"作品已撤回、目录过期或未经发布审核");
   var asset=entry.Package;string digest=Catalog.Digest;long size=(long)asset["size"];
   // Stream large packages directly to exclusive staging without a RAM copy.
   Uri uri=new Uri((string)asset["url"]);for(int redirects=0;redirects<4;redirects++){
    using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancel)){
     int code=(int)response.StatusCode;if(code==301||code==302||code==303||code==307||code==308){MarketCatalog.Require(response.Headers.Location!=null,"下载重定向缺少目标");uri=new Uri(uri,response.Headers.Location);Redirect(uri);continue;}
     if(code==403||code==429)throw new HttpRequestException("GitHub 限流或拒绝下载，请稍后重试。");response.EnsureSuccessStatusCode();
     MarketCatalog.Require(!response.Content.Headers.ContentLength.HasValue||response.Content.Headers.ContentLength==size,"附件大小与目录不一致");
     bool created=false;try {
      using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create()){
       created=true;var buffer=new byte[65536];long total=0;int n;while((n=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0){total+=n;MarketCatalog.Require(total<=size&&total<=MarketCatalog.MaxPackageBytes,"附件超过目录预算");sha.TransformBlock(buffer,0,n,buffer,0);await output.WriteAsync(buffer,0,n,cancel);}
       sha.TransformFinalBlock(Array.Empty<byte>(),0,0);MarketCatalog.Require(total==size&&MarketCatalog.Hex(sha.Hash)==(string)asset["sha256"],"下载附件摘要不匹配");output.Flush(true);
      }
      MarketCatalog.Require(Catalog.Digest==digest&&Catalog.Installable(entry,DateTimeOffset.UtcNow),"下载过程中目录已变化，请重新选择");return;
     }catch{if(created&&File.Exists(target))File.Delete(target);throw;}
    }
   }throw new HttpRequestException("GitHub 下载重定向次数过多");
  }
  public static void AtomicWrite(string path,string text) {
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(output,new UTF8Encoding(false))){writer.Write(text);writer.Flush();output.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public void Dispose(){http.Dispose();}
 }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Content {
 // Metadata never grants a content capability. ImportContent remains the
 // authority for archive admission and isolated script execution.
 public sealed class MarketTrust {
  public string Repository,CatalogUrl,SignatureUrl,KeyId,Modulus,Exponent;
  public static MarketTrust Parse(string json) {
   var o=MarketCatalog.ParseObject(json);
   var t=new MarketTrust{Repository=(string)o["repository"],CatalogUrl=(string)o["catalogUrl"],SignatureUrl=(string)o["signatureUrl"],KeyId=(string)o["keyId"],Modulus=(string)o["modulus"],Exponent=(string)o["exponent"]};
   MarketCatalog.RepositoryName(t.Repository);
   MarketCatalog.Require(t.KeyId!=null&&t.KeyId.Length<=100,"市场信任配置缺少密钥");
   MarketCatalog.OfficialRelease(t.CatalogUrl,t.Repository);MarketCatalog.OfficialRelease(t.SignatureUrl,t.Repository);
   using(var rsa=RSA.Create()){rsa.ImportParameters(new RSAParameters{Modulus=Convert.FromBase64String(t.Modulus),Exponent=Convert.FromBase64String(t.Exponent)});MarketCatalog.Require(rsa.KeySize>=2048,"市场密钥过短");}
   return t;
  }
 }
 public sealed class MarketEntry {
  internal JObject Data;internal string CatalogDigest;
  public string Key=>(string)Data["key"];
  public string Title=>(string)Data["title"];
  public string Repository=>(string)Data["repository"];
  public string Version=>(string)Data["build"]["version"];
  public string Identity=>(string)Data["build"]["identity"];
  public string Sdk=>(string)Data["sdk"];
  public string Status=>(string)Data["status"];
  public string Description=>(string)Data["description"]??"作者未提供作品说明。";
  public string Authors=>string.Join("、",((JArray)Data["attribution"]).Select(a=>(string)a["name"]));
  public string Licenses=>string.Join("、",((JArray)Data["attribution"]).Select(a=>(string)a["license"]).Distinct());
  public string[] Capabilities=>((JArray)Data["capabilities"]).Values<string>().ToArray();
  public string Preview=>(string)Data["preview"];
  public string Feedback=>Data["comments"] is JObject c?(string)c["url"]:Data["discussion"] is JObject d?(string)d["url"]:null;
  public string Source=>"https://github.com/"+Repository;
  public DateTimeOffset FirstListed=>MarketCatalog.Date((string)Data["firstListedAt"]);
  public long? Downloads7Days=>Data["popularity"]?["downloads7d"]?.Type==JTokenType.Integer?(long?)Data["popularity"]["downloads7d"]:null;
  public string PopularityLabel=>Downloads7Days.HasValue?"近7日下载 "+Downloads7Days.Value.ToString(CultureInfo.InvariantCulture)+" 次":"近7日下载数据不足";
  internal JObject Package=>Data["admission"] is JObject admission?((JArray)Data["build"]["assets"]).OfType<JObject>().Single(a=>(long)a["id"]==(long)admission["packageAssetId"]):null;
 }
 public sealed class MarketCatalog {
  public const long MaxIndexBytes=8*1024*1024,MaxPackageBytes=1024L*1024*1024;
  public readonly MarketEntry[] Entries;public readonly long Generation;public readonly string Digest;public readonly DateTimeOffset Expires;
  readonly HashSet<string> revokedWorks,revokedBuilds;
  public bool Fresh(DateTimeOffset now)=>now<Expires;
  MarketCatalog(JObject o,string digest) {
   Generation=(long)o["generation"];Digest=digest;Expires=Date((string)o["expiresAt"]);
   revokedWorks=new HashSet<string>(((JArray)o["revocations"]["works"]).Values<string>(),StringComparer.Ordinal);
   revokedBuilds=new HashSet<string>(((JArray)o["revocations"]["builds"]).Values<string>(),StringComparer.Ordinal);
   Entries=((JArray)o["entries"]).OfType<JObject>().Select(e=>new MarketEntry{Data=(JObject)e.DeepClone(),CatalogDigest=digest}).ToArray();
  }
  public bool Installable(MarketEntry e,DateTimeOffset now)=>Fresh(now)&&e.CatalogDigest==Digest&&e.Status=="published"&&e.Package!=null&&!revokedWorks.Contains(e.Key)&&!revokedBuilds.Contains(e.Identity);
  public MarketEntry[] Search(string query,string filter,bool hot) {
   var list=Entries.Where(e=>string.IsNullOrWhiteSpace(query)||(e.Title+" "+e.Authors+" "+e.Description+" "+e.Repository).IndexOf(query.Trim(),StringComparison.OrdinalIgnoreCase)>=0);
   if(filter=="可安装")list=list.Where(e=>Installable(e,DateTimeOffset.UtcNow));
   else if(filter=="互动")list=list.Where(e=>e.Capabilities.Contains("pointer-input")||e.Capabilities.Contains("input"));
   else if(filter=="声音")list=list.Where(e=>e.Capabilities.Contains("sound-playback"));
   else if(filter=="已下架")list=list.Where(e=>e.Status=="revoked");
   return (hot?list.OrderByDescending(e=>e.Downloads7Days.HasValue).ThenByDescending(e=>e.Downloads7Days??0).ThenBy(e=>e.Key,StringComparer.Ordinal):list.OrderByDescending(e=>e.FirstListed).ThenBy(e=>e.Key,StringComparer.Ordinal)).ToArray();
  }
  public static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static string Text(JObject o,string key,int max=4096){Require(o[key]?.Type==JTokenType.String,"市场字段无效："+key);string s=(string)o[key];Require(s.Length>0&&s.Length<=max,"市场字段长度无效："+key);return s;}
  static long Number(JObject o,string key){Require(o[key]?.Type==JTokenType.Integer,"市场数字无效："+key);long n=(long)o[key];Require(n>0,"市场数字无效："+key);return n;}
  static bool Hash(string s)=>s!=null&&Regex.IsMatch(s,"\\A[0-9a-f]{64}\\z");
  public static DateTimeOffset Date(string s){Require(DateTimeOffset.TryParseExact(s,"yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var d),"市场时间无效");return d;}
  public static JObject ParseObject(string json) {
   using(var reader=new JsonTextReader(new StringReader(json)){DateParseHandling=DateParseHandling.None,MaxDepth=32}){
    var value=JToken.ReadFrom(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Require(value is JObject&&!reader.Read(),"市场 JSON 结构无效");return (JObject)value;
   }
  }
  static Uri Https(string value){Require(Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo==""&&u.Fragment=="","市场网址无效");return u;}
  public static void RepositoryName(string repository){Require(repository!=null&&Regex.IsMatch(repository,"\\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\\z")&&!repository.Split('/').Any(p=>p=="."||p==".."),"仓库身份无效");}
  public static void OfficialRelease(string value,string repository){RepositoryName(repository);var u=Https(value);Require(u.Host=="github.com"&&u.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.Ordinal)&&u.Query=="","市场目录必须来自受信任社区 Release");}
  public static string BrowserUrl(string value) {
   var u=Https(value);Require(u.Host=="github.com"&&u.Query==""&&!u.AbsolutePath.Contains("%")&&Regex.IsMatch(u.AbsolutePath,"\\A/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/discussions/[1-9][0-9]*|/issues/(?:new|[1-9][0-9]*))?\\z"),"市场外部链接不受支持");return u.AbsoluteUri;
  }
  public static void PreviewUrl(string value){var u=Https(value);Require((u.Host=="raw.githubusercontent.com"||u.Host=="user-images.githubusercontent.com"||u.Host=="github.com"&&u.AbsolutePath.StartsWith("/user-attachments/assets/",StringComparison.Ordinal))&&u.Query=="","预览图来源不受支持");}
  static void Build(JObject build,string repository) {
   Text(build,"version",100);Text(build,"tag",100);Number(build,"releaseId");Require(Hash(Text(build,"identity",64)),"市场构建摘要无效");
   Require(build["assets"] is JArray a&&a.Count>0&&a.Count<=16,"市场附件数无效");var seen=new HashSet<long>();
   foreach(var asset in ((JArray)build["assets"]).OfType<JObject>()) {
    Require(seen.Add(Number(asset,"id")),"重复市场附件");string name=Text(asset,"name",200);Require(Regex.IsMatch(name,"\\A[A-Za-z0-9][A-Za-z0-9._-]{0,199}\\z"),"附件名称无效");
    Require(Number(asset,"size")<=MaxPackageBytes&&Hash(Text(asset,"sha256",64)),"市场附件预算或摘要无效");
    string expected="https://github.com/"+repository+"/releases/download/"+Uri.EscapeDataString((string)build["tag"])+"/"+Uri.EscapeDataString(name);Require(Text(asset,"url")==expected,"附件必须属于确切来源 Release");
   }
   Require(seen.Count==((JArray)build["assets"]).Count,"附件必须是对象");
  }
  public static MarketCatalog Verify(byte[] bytes,string signature,MarketTrust trust,long watermark,string priorDigest,DateTimeOffset now,bool offline=false) {
   Require(bytes!=null&&bytes.Length>0&&bytes.Length<=MaxIndexBytes,"市场目录大小无效");var sig=ParseObject(signature);string digest;using(var sha=SHA256.Create())digest=Hex(sha.ComputeHash(bytes));
   Require((string)sig["algorithm"]=="RSA-PKCS1-SHA256"&&(string)sig["keyId"]==trust.KeyId&&(string)sig["sha256"]==digest,"目录签名身份或摘要不匹配");
   byte[] prefix=Encoding.UTF8.GetBytes("spatial-market-published-v2\0"),message=new byte[prefix.Length+bytes.Length];Buffer.BlockCopy(prefix,0,message,0,prefix.Length);Buffer.BlockCopy(bytes,0,message,prefix.Length,bytes.Length);
   using(var rsa=RSA.Create()){rsa.ImportParameters(new RSAParameters{Modulus=Convert.FromBase64String(trust.Modulus),Exponent=Convert.FromBase64String(trust.Exponent)});Require(rsa.VerifyData(message,Convert.FromBase64String((string)sig["signature"]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),"市场目录签名无效");}
   var o=ParseObject(new UTF8Encoding(false,true).GetString(bytes));Require((int?)o["schemaVersion"]==2&&(string)o["kind"]=="published","不是已审核发布目录");long generation=Number(o,"generation");Require(generation>=watermark&&(generation!=watermark||priorDigest==null||digest==priorDigest),"目录倒退或同代目录冲突");
   var generated=Date(Text(o,"generatedAt"));var expires=Date(Text(o,"expiresAt"));Require(generated<=now&&expires>generated&&expires-generated<=TimeSpan.FromDays(8),"目录时间范围无效");if(!offline)Require(now<expires,"市场目录已过期，请稍后刷新");
   Require(o["entries"] is JArray entries&&entries.Count<=10000&&o["revocations"] is JObject rev&&rev["works"] is JArray&&rev["builds"] is JArray,"目录条目或撤回信息无效");
   var keys=new HashSet<string>();var works=new HashSet<string>(StringComparer.Ordinal);foreach(var token in (JArray)o["entries"]) {
    Require(token is JObject,"市场条目无效");var e=(JObject)token;long repoId=Number(e,"repositoryId");Number(e,"ownerId");string repo=Text(e,"repository",200);RepositoryName(repo);
    string work=Text(e,"workId",64);Require(Regex.IsMatch(work,"\\A[a-z0-9][a-z0-9-]{0,63}\\z"),"作品身份无效");string key=Text(e,"key",100);Require(key==repoId.ToString(CultureInfo.InvariantCulture)+":"+work&&keys.Add(key),"作品身份冲突");
    Require(works.Add(work),"作品 GUID 不得跨仓库重复，包括已撤回作品");
    Text(e,"title",200);Text(e,"sdk",100);Date(Text(e,"firstListedAt"));Require(e["capabilities"] is JArray&&e["attribution"] is JArray attr&&attr.Count>0&&attr.Count<=100,"署名或能力无效");
    foreach(var t in (JArray)e["capabilities"])Require(t.Type==JTokenType.String&&((string)t).Length<=64,"能力字段无效");
    foreach(var t in (JArray)e["attribution"]){Require(t is JObject,"署名无效");Text((JObject)t,"name",200);Text((JObject)t,"license",200);Number((JObject)t,"githubUserId");}
    Require(e["build"] is JObject,"构建无效");Build((JObject)e["build"],repo);string status=Text(e,"status",40);Require(status=="published"||status=="revoked","未知市场发布状态");
    if(e["description"]!=null)Text(e,"description",4096);if(e["preview"]!=null)PreviewUrl(Text(e,"preview"));
    if(e["discussion"] is JObject discussion){long number=Number(discussion,"number");Require(Text(discussion,"url")=="https://github.com/"+trust.Repository+"/discussions/"+number.ToString(CultureInfo.InvariantCulture),"评论必须关联受信任社区讨论");}
    if(e["comments"]!=null){Require(e["comments"] is JObject,"评论映射无效");var comments=(JObject)e["comments"];long number=Number(comments,"number");Require(Text(comments,"kind",20)=="issues"&&Text(comments,"url")=="https://github.com/"+trust.Repository+"/issues/"+number.ToString(CultureInfo.InvariantCulture),"评论必须关联受信任社区作品 Issue");}
    if(e["popularity"] is JObject popularity)Require(popularity["downloads7d"]==null||popularity["downloads7d"].Type==JTokenType.Null||popularity["downloads7d"].Type==JTokenType.Integer&&(long)popularity["downloads7d"]>=0,"热度数据无效");
    if(status=="published"){
     Require(e["admission"] is JObject,"缺少审核绑定");var admission=(JObject)e["admission"];Require((int?)admission["schemaVersion"]==5&&(string)admission["sdkVersion"]=="0.4.0-preview.1"&&(int?)admission["sceneCommandAbi"]==1&&(string)admission["policy"]=="native-admission-v1"&&(string)admission["buildIdentity"]==(string)e["build"]["identity"],"SDK 或准入版本不兼容");
     long packageId=Number(admission,"packageAssetId");var packages=((JArray)e["build"]["assets"]).OfType<JObject>().Where(asset=>(long)asset["id"]==packageId).ToArray();Require(packages.Length==1&&((string)packages[0]["name"]).EndsWith(".spatial",StringComparison.Ordinal),"缺少精确作品包");
    }
   }return new MarketCatalog(o,digest);
  }
  public static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
 }
}

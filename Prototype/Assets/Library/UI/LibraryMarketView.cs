using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CalibrationDemoView {
 bool libraryMarket,marketBusy,marketInitialized,marketHot=true;
 string marketSearch="",marketFilter="全部",marketSelected,marketError;
 GitHubMarketClient marketClient;JObject marketInstalled=new JObject();int marketPage=1;
 LibraryMarketLifetime marketLifetime;
 CancellationToken MarketToken=>marketLifetime?marketLifetime.Cancellation.Token:CancellationToken.None;
 RectTransform marketList,marketDetail;TMP_Text marketNotice;Texture2D marketPreview;int marketDetailGeneration;
 string MarketDirectory=>Path.Combine(DesktopRuntime.ContentRoot,"market");
 string MarketInstalledPath=>Path.Combine(MarketDirectory,"installed.json");
 void InitializeMarket(){
  if(marketInitialized)return;marketInitialized=true;
  try {
   var config=Resources.Load<TextAsset>("Library/MarketTrust");if(!config)throw new InvalidDataException("此发行版缺少市场信任配置；本地作品仍可使用。");
   marketClient=new GitHubMarketClient(MarketTrust.Parse(config.text),MarketDirectory);
   marketLifetime=gameObject.AddComponent<LibraryMarketLifetime>();marketLifetime.Client=marketClient;
   if(File.Exists(MarketInstalledPath))marketInstalled=MarketCatalog.ParseObject(File.ReadAllText(MarketInstalledPath));
  }catch(Exception error){marketError="市场初始化失败："+error.GetBaseException().Message;Debug.LogWarning(error);}
 }
 TMP_Text MarketText(Transform parent,string text,float x,float y,float width,float height,int size=14){var label=ui.Text(parent,text,x,y,width,height,size,CalibrationWidgets.Ink);label.richText=false;label.enableWordWrapping=true;return label;}
 void BuildMarketSurface(){
  InitializeMarket();detail=null;libraryImport=null;libraryGrid=null;
  float rail=pageWidth<850?280:300,w=pageWidth-rail-24;
  ui.Button(page,"本地作品",0,0,104,()=>{libraryMarket=false;marketDetailGeneration++;if(marketPreview){Destroy(marketPreview);marketPreview=null;}Build();});
  var refresh=ui.Button(page,marketBusy?"处理中…":"刷新市场",116,0,104,RefreshMarket);refresh.SetInteractable(!marketBusy&&marketClient!=null);
  bool narrow=w<480;float controlsY=narrow?48:0;
  Dropdown(page,"市场筛选",narrow?0:232,controlsY,104,()=>new[]{"全部","可安装","互动","声音","已下架"}.Select(name=>new Choice(name,()=>{marketFilter=name;marketPage=1;Build();})).ToList(),marketFilter);
  Dropdown(page,"市场排序",narrow?116:348,controlsY,116,()=>new[]{new Choice("近7日热度",()=>{marketHot=true;marketPage=1;Build();}),new Choice("最近上架",()=>{marketHot=false;marketPage=1;Build();})}.ToList(),marketHot?"近7日热度":"最近上架");
  float searchY=narrow?104:56;
  var input=ui.Input(page,marketSearch,0,searchY,w);input.name="Search community artworks";
  var hint=MarketText(input.transform,"搜索作品、作者或来源",10,0,w-20,40);input.placeholder=hint;
  input.onValueChanged.AddListener(value=>{marketSearch=value;marketPage=1;RebuildMarketList();});
  float h=Mathf.Max(120,pageHeight-256-(narrow?48:0));marketList=ui.Rect(page,"Market artworks",0,searchY+52,w,h);
  var viewport=ui.Panel(page,"Market detail viewport",pageWidth-rail,56,rail,pageHeight-108,Color.white);viewport.gameObject.AddComponent<RectMask2D>();
  marketDetail=ui.Rect(viewport,"Market selected detail",0,0,rail-12,pageHeight-108);var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.content=marketDetail;scroll.viewport=viewport;scroll.horizontal=false;scroll.scrollSensitivity=50;scroll.movementType=ScrollRect.MovementType.Clamped;
  marketNotice=MarketText(page,marketError??marketClient?.Notice??"市场不可用。",0,pageHeight-132,pageWidth,56,13);
  MarketText(page,"热度为 GitHub 附件近7日下载增量，含重复下载及更新；不是评分或安装人数。缺测不记零，GitHub 不提供可靠去刷数据。",0,pageHeight-68,pageWidth,52,12);
  RebuildMarketList();RefreshMarketDetails();
 }
 void RebuildMarketList(){
  if(!libraryMarket||!marketList)return;Clear(marketList);
  var entries=marketClient?.Catalog?.Search(marketSearch,marketFilter,marketHot)??Array.Empty<MarketEntry>();
  int perPage=Mathf.Max(1,Mathf.FloorToInt((marketList.sizeDelta.y-48)/92));int pages=Mathf.Max(1,(entries.Length+perPage-1)/perPage);marketPage=Mathf.Clamp(marketPage,1,pages);
  var shown=entries.Skip((marketPage-1)*perPage).Take(perPage).ToArray();float w=marketList.sizeDelta.x;
  for(int i=0;i<shown.Length;i++){
   var entry=shown[i];float y=i*92;var row=ui.Panel(marketList,"Market "+entry.Key,0,y,w,80,marketSelected==entry.Key?new Color32(228,239,255,255):Color.white);
   MarketText(row,entry.Title,12,4,w-24,28,18);MarketText(row,entry.Authors+" · "+entry.Version+" · "+(entry.Status=="revoked"?"已下架":entry.PopularityLabel),12,36,w-24,36,12);
   var click=row.gameObject.AddComponent<UnityEngine.UI.Button>();click.targetGraphic=row.GetComponent<Graphic>();click.onClick.AddListener(()=>{marketSelected=entry.Key;RebuildMarketList();RefreshMarketDetails();});
  }
  if(entries.Length==0)MarketText(marketList,marketClient?.Catalog==null?"尚无已验证目录。点击刷新市场；本地作品和蓝图可继续使用。":"没有符合条件的作品。可尝试其他关键词或筛选。",12,16,w-24,104,16);
  float footer=marketList.sizeDelta.y-44;
  var prev=ui.Button(marketList,"上一页",0,footer,100,()=>{marketPage--;RebuildMarketList();});prev.SetInteractable(marketPage>1);
  MarketText(marketList,marketPage+" / "+pages+" · "+entries.Length+" 件",112,footer,w-224,36,13);
  var next=ui.Button(marketList,"下一页",w-100,footer,100,()=>{marketPage++;RebuildMarketList();});next.SetInteractable(marketPage<pages);
 }
 Artwork MarketLocal(MarketEntry e){string work=(string)marketInstalled[e.Key]?["work_id"];return work==null?null:library.Catalog.FirstOrDefault(a=>a.Id==work&&a.IsContent);}
 void RefreshMarketDetails(){
  if(!libraryMarket||!marketDetail)return;Clear(marketDetail);int generation=++marketDetailGeneration;if(marketPreview){Destroy(marketPreview);marketPreview=null;}
  if(marketNotice)marketNotice.text=marketError??marketClient?.Notice??"市场不可用。";
  var e=marketClient?.Catalog?.Entries.FirstOrDefault(entry=>entry.Key==marketSelected);float w=marketDetail.sizeDelta.x,y=16;
  if(e==null){MarketText(marketDetail,"选择社区作品",16,y,w-32,40,20);MarketText(marketDetail,"安装后可在本地作品库预览并显式应用。更新不会切换正在播放的修订。\n\n每件作品关联社区 GitHub Issue。公开社区可匿名查看；参与需在浏览器登录 GitHub。",16,y+56,w-32,220);return;}
  MarketText(marketDetail,e.Title,16,y,w-32,60,22);y+=72;
  if(e.Preview!=null){var frame=ui.Rect(marketDetail,"Market preview",16,y,w-32,(w-32)*9/16);var image=frame.gameObject.AddComponent<RawImage>();image.color=Color.white;LoadMarketPreview(e,image,generation);y+=(w-32)*9/16+12;}
  MarketText(marketDetail,"作者："+e.Authors+"\n版本："+e.Version+"\nSDK："+e.Sdk+"\n许可："+e.Licenses+"\n来源："+e.Repository+"\n"+e.PopularityLabel,16,y,w-32,170);y+=182;
  float descriptionHeight=Mathf.Clamp(e.Description.Length*9/(Mathf.Max(1,w-32)/14)*20+36,76,900);MarketText(marketDetail,e.Description,16,y,w-32,descriptionHeight);y+=descriptionHeight+12;
  var local=MarketLocal(e);bool same=local!=null&&(string)marketInstalled[e.Key]?["identity"]==e.Identity&&(string)marketInstalled[e.Key]?["build_id"]==local.BuildId;
  var install=ui.Button(marketDetail,marketBusy?"处理中…":same?"已安装此版本":local==null?"下载并安装":"下载并更新",16,y,w-32,()=>InstallMarket(e));install.SetInteractable(!marketBusy&&!contentOperation&&!same&&ContentClient?.Available==true&&marketClient.Catalog.Installable(e,DateTimeOffset.UtcNow));y+=48;
  if(e.Status=="revoked"){MarketText(marketDetail,"此作品已下架，禁止新安装。本地修订不会被市场自动删除。",16,y,w-32,72);y+=84;}
  if(local!=null){ui.Button(marketDetail,"在本地作品库查看",16,y,w-32,()=>{libraryMarket=false;library.Select(local);Build();});y+=48;ui.Button(marketDetail,"卸载本地作品",16,y,w-32,()=>ConfirmLibraryRemoval(local.Id));y+=48;}
  ui.Button(marketDetail,"查看作品来源",16,y,w-32,()=>OpenMarketLink(e.Source));y+=48;
  var comment=ui.Button(marketDetail,e.Feedback==null?"评论尚未开通":"查看与参与评论",16,y,w-32,()=>OpenMarketLink(e.Feedback));comment.SetInteractable(e.Feedback!=null);y+=48;
  ui.Button(marketDetail,"举报作品 / 许可问题",16,y,w-32,()=>{GUIUtility.systemCopyBuffer="市场作品 "+e.Key+"\n构建 "+e.Identity;OpenMarketLink("https://github.com/"+marketClient.Repository+"/issues/new");});y+=48;
  MarketText(marketDetail,"举报已复制作品身份；请在 GitHub 粘贴说明。维护者可锁定、隐藏或删除讨论内容。联网失败时请在浏览器重试；安装不自动应用。",16,y,w-32,112,12);y+=124;
  marketDetail.sizeDelta=new Vector2(w,Mathf.Max(((RectTransform)marketDetail.parent).rect.height,y));
 }
 async void RefreshMarket(){
  if(marketBusy||marketClient==null)return;marketBusy=true;marketError=null;Build();
  try{await marketClient.Refresh(MarketToken);}
  catch(Exception failure){marketError="市场刷新失败："+failure.GetBaseException().Message+(marketClient.Catalog==null?"":" 已保留上次验证缓存。");Debug.LogWarning(failure);}
  finally{marketBusy=false;if(this&&currentPage=="作品库"&&libraryMarket)Build();}
 }
 async void InstallMarket(MarketEntry entry){
  if(marketBusy||contentOperation||ContentClient?.Available!=true)return;var local=MarketLocal(entry);string expectedWork=local?.Id,expectedRevision=local?.RevisionId;marketBusy=true;marketError=null;string path=Path.Combine(MarketDirectory,Guid.NewGuid().ToString("N")+".spatial");Build();
  try{
   await marketClient.Download(entry,path,MarketToken);if(!this) return;
   if(contentOperation)throw new InvalidOperationException("其他导入正在进行，请稍后重试。");
   var current=MarketLocal(entry);if(current?.Id!=expectedWork||current?.RevisionId!=expectedRevision)throw new InvalidOperationException("下载期间本地作品已更新或卸载，请重新选择操作。");bool completed=false;
   await ImportLibraryContentAsync(new[]{path},expectedWork,expectedRevision,work=>{marketInstalled[entry.Key]=new JObject{["work_id"]=work,["identity"]=entry.Identity,["build_id"]=ArtworkCatalog.Find(work).BuildId};GitHubMarketClient.AtomicWrite(MarketInstalledPath,marketInstalled.ToString(Formatting.None));completed=true;});
   marketError=completed?"已通过本地准入并安装。请到本地作品库预览、调整声音并显式应用。":libraryActionError;
  }catch(Exception failure){marketError="市场安装未完成："+failure.GetBaseException().Message;Debug.LogWarning(failure);}
  finally{if(File.Exists(path))File.Delete(path);marketBusy=false;if(this&&currentPage=="作品库"&&libraryMarket)Build();}
 }
 void OpenMarketLink(string url){try{Application.OpenURL(MarketCatalog.BrowserUrl(url));}catch(Exception error){marketError="无法打开 GitHub 页面："+error.Message;RefreshMarketDetails();}}
 async void LoadMarketPreview(MarketEntry entry,RawImage image,int generation){
  try{
   var bytes=await marketClient.Preview(entry.Preview,MarketToken);
   // Read PNG dimensions before native image decoding; cap decoded allocation.
   MarketCatalog.Require(bytes.Length>=24&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71&&bytes[12]==73&&bytes[13]==72&&bytes[14]==68&&bytes[15]==82,"预览图需要 PNG 格式");
   uint width=((uint)bytes[16]<<24)|((uint)bytes[17]<<16)|((uint)bytes[18]<<8)|bytes[19],height=((uint)bytes[20]<<24)|((uint)bytes[21]<<16)|((uint)bytes[22]<<8)|bytes[23];MarketCatalog.Require(width>0&&height>0&&width<=2048&&height<=2048,"预览图尺寸超过预算");
   if(!this||!image||generation!=marketDetailGeneration)return;var texture=new Texture2D(2,2);if(!texture.LoadImage(bytes,true)){Destroy(texture);throw new InvalidDataException("预览图无法解码");}marketPreview=texture;image.texture=texture;
  }catch(Exception error){if(this&&image&&generation==marketDetailGeneration)MarketText(image.transform,"预览图不可用："+error.GetBaseException().Message,8,8,image.rectTransform.sizeDelta.x-16,image.rectTransform.sizeDelta.y-16,12);}
 }
}

public sealed class LibraryMarketLifetime:MonoBehaviour {
 public GitHubMarketClient Client;
 public readonly CancellationTokenSource Cancellation=new CancellationTokenSource();
 void OnDestroy(){Cancellation.Cancel();Client?.Dispose();Cancellation.Dispose();}
}

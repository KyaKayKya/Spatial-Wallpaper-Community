using System;
using System.Collections.Generic;
using System.Linq;
namespace SpatialWallpaper.Library {
 public enum ArtworkSource { BuiltinNative, SdkLegacy, SdkNative }
 public sealed class Artwork {
  public string Id {get;}
  public string Name {get;}
  public string Description {get;}
  public string Category {get;}
  readonly string sceneId;
  public ArtworkSource Source {get;private set;}=ArtworkSource.BuiltinNative;
  public bool IsContent => Source!=ArtworkSource.BuiltinNative;
  public string RevisionId {get;private set;}
  public string BuildId {get;private set;}
  public string CoverId {get;private set;}
  public string ScenePath => IsContent?null:"Assets/Library/Scenes/"+(sceneId??Id)+".unity";
  public Artwork(string id,string name,string category,string description,string sceneId=null){Id=id;Name=name;Category=category;Description=description;this.sceneId=sceneId;}
  public static Artwork FromContent(string work,string name,string revision,string build,string cover=null,bool native=false){
   if(!Guid.TryParseExact(work,"D",out var id)||id.ToString("D")!=work||!Guid.TryParseExact(revision,"D",out var rev)||rev.ToString("D")!=revision||build==null||build.Length!=64)throw new ArgumentException("invalid-content-artwork");
   return new Artwork(work,name,native?"原生 SDK":"SDK",native?"Unity 原生渲染、作者脚本隔离运行的 schema5 预览版作品。更新后须显式应用。":"隔离运行的 SDK 作品。更新后须显式应用。"){Source=native?ArtworkSource.SdkNative:ArtworkSource.SdkLegacy,RevisionId=revision,BuildId=build,CoverId=cover};
  }
 }
 public static class ArtworkCatalog {
  public static Artwork NoWallpaper {get;}=new Artwork("no-wallpaper","无壁纸","","恢复原来的 系统壁纸");
  public static readonly Artwork BlueprintTemplate=new Artwork("blueprint","Minecraft 蓝图模板","创作","导入蓝图，创建自己的空间壁纸。");
  static readonly IReadOnlyList<Artwork> builtins=Array.AsReadOnly(new[]{
   new Artwork("showcase","白色展示柜","陈设","双顶灯照亮的白色展示空间，透过固定窗口观察摆件的层次。"),
   new Artwork("pool","池核","空间","瓷砖、水面与浮动泳圈组成的静谧泳池空间。"),
   new Artwork("train","复古车厢","空间","温暖的复古包厢，窗外景色随着旅程缓缓流动。"),
   new Artwork("parking","阈限空间","空间","延伸的地面与灯阵，在寂静的停车空间中观察纵深。"),
   new Artwork("minishotgame","先行者","互动","固定空间窗口中的出屏靶盘与深度网格。"),
   new Artwork("limpool","Limpool","空间","天光、倒影与水光交织的静谧室内水池。"),
   new Artwork("rainycafe","雨夜咖啡馆","空间","窗外细雨与室内烛光相伴的温暖咖啡馆。"),
   new Artwork("mita","米塔","互动","米塔从暗处出现，轻点脸颊，回应她的目光与原版小动作。")});
  public static Func<IReadOnlyList<Artwork>> Additional;
  public static Func<string,Artwork> ResolveAdditional;
  public static Func<IReadOnlyList<Artwork>> Content;
  // CoverId selects only a thumbnail. Installing content must never substitute
  // a builtin's identity, scene or applied selection.
  public static IReadOnlyList<Artwork> All => builtins.Concat(Additional?.Invoke()??Array.Empty<Artwork>()).Concat(Content?.Invoke()??Array.Empty<Artwork>()).ToArray();
  public static IReadOnlyList<Artwork> Builtins=>builtins;
  public static Artwork Find(string id)=>id=="blueprint"?BlueprintTemplate:builtins.FirstOrDefault(x=>x.Id==id)??ResolveAdditional?.Invoke(id)??Content?.Invoke().FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("unknown-artwork");
 }
}

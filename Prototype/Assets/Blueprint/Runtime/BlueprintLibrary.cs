using System;
using System.IO;
using System.Linq;
using SpatialWallpaper.Library;
using UnityEngine;
using System.Collections.Generic;
namespace SpatialWallpaper.Blueprint {
 public static class BlueprintLibrary {
  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]static void Register(){ArtworkCatalog.Additional=()=>Works();ArtworkCatalog.ResolveAdditional=Find;}
  public static string Root=>BlueprintScene.Argument("--blueprint-data-root")??Path.Combine(SpatialWallpaper.Desktop.DesktopRuntime.DataRoot,"blueprints");
  public static BlueprintWorkspace Store=>new BlueprintWorkspace(Root);
  public static string PendingArtwork;
  static readonly Dictionary<string,Texture2D> covers=new Dictionary<string,Texture2D>();static readonly Queue<string> coverOrder=new Queue<string>();
  static string CoverPath(string id){var parts=id.Split('-');if(parts.Length!=3||parts[0]!="blueprint"||!Guid.TryParseExact(parts[1],"N",out _)||!Guid.TryParseExact(parts[2],"N",out _))return null;return Path.Combine(Root,"covers",parts[1]+"-"+parts[2]+".png");}
  public static Texture2D Cover(string id){if(!id.StartsWith("blueprint-",StringComparison.Ordinal))return null;if(covers.TryGetValue(id,out var found))return found;string path=CoverPath(id);if(path==null||!File.Exists(path)||new FileInfo(path).Length>2*1024*1024)return null;var image=new Texture2D(2,2,TextureFormat.RGB24,false);if(!image.LoadImage(File.ReadAllBytes(path),true)){UnityEngine.Object.Destroy(image);return null;}covers[id]=image;coverOrder.Enqueue(id);while(coverOrder.Count>24){string old=coverOrder.Dequeue();if(covers.TryGetValue(old,out var texture)){UnityEngine.Object.Destroy(texture);covers.Remove(old);}}return image;}
  public static void SaveCover(BlueprintWorkRevision revision,RenderTexture source){if(!source)return;string id=Artwork(revision).Id,path=CoverPath(id);RenderTexture previous=RenderTexture.active;var target=RenderTexture.GetTemporary(512,288,0);Texture2D texture=null;try{float ratio=(float)source.width/source.height/(512f/288);var scale=ratio<1?new Vector2(1,ratio):new Vector2(1/ratio,1);Graphics.Blit(source,target,scale,(Vector2.one-scale)*.5f);RenderTexture.active=target;texture=new Texture2D(512,288,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,512,288),0,0);texture.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,texture.EncodeToPNG());if(covers.TryGetValue(id,out var old)){UnityEngine.Object.Destroy(old);covers.Remove(id);}}catch(Exception e){Debug.LogWarning("Blueprint cover: "+e.Message);}finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(texture)UnityEngine.Object.Destroy(texture);}}
  static Artwork[] cached=new Artwork[0];static string cachedRoot;static DateTime timestamp;static long indexLength=-1;
  public static Artwork[] Works(){try{string root=Root;var info=new FileInfo(Path.Combine(root,"index.json"));DateTime time=info.Exists?info.LastWriteTimeUtc:DateTime.MinValue;long length=info.Exists?info.Length:0;if(cachedRoot!=root||time!=timestamp||length!=indexLength){cached=Store.List().Select(Artwork).ToArray();cachedRoot=root;timestamp=time;indexLength=length;}return cached;}catch(Exception e){UnityEngine.Debug.LogWarning("Blueprint workspace: "+e.Message);return new Artwork[0];}}
  public static Artwork Artwork(BlueprintWorkRevision revision)=>new Artwork("blueprint-"+revision.WorkId+"-"+revision.RevisionId,revision.Name,"Minecraft 蓝图","本机蓝图作品 · 可调整构图、天气与世界时间。","blueprint");
  public static BlueprintWorkRevision Resolve(string id){if(id==null||!id.StartsWith("blueprint-",StringComparison.Ordinal))return null;var parts=id.Split('-');if(parts.Length!=3)throw new BlueprintImportException("workspace-artwork-id");return Store.Revision(parts[1],parts[2]);}
  public static Artwork Find(string id){var revision=Resolve(id);return revision==null?null:Artwork(revision);}
 }
}

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialWallpaper.Library;
static class LibraryInteractionChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 sealed class Host:IWallpaperHost {
  public bool Available=>true;public string ActiveArtworkId{get;private set;}="pool";public int Calls;public WallpaperRequest Request;
  public TaskCompletionSource<WallpaperResult> Pending=new TaskCompletionSource<WallpaperResult>();
  public async Task<WallpaperResult> ApplyAsync(WallpaperRequest request){Calls++;Request=request;var result=await Pending.Task;if(result.Succeeded)ActiveArtworkId=request.ArtworkId;return result;}
  public Task<WallpaperResult> StopAsync()=>throw new NotSupportedException();
 }
 static void Main(){PresetChecks();Run().GetAwaiter().GetResult();}
 static void PresetChecks(){
  var memory=new System.Collections.Generic.Dictionary<string,string>();var b=new LibraryBindings(k=>memory.ContainsKey(k)?memory[k]:null,(k,v)=>memory[k]=v);
  var method=typeof(LibraryBindings).GetMethod("ResolvePreset");Check(method!=null,"Missing compatible default preset resolution");
  Func<string[],string,string> resolve=(ids,active)=>(string)method.Invoke(b,new object[]{"art","screen",ids,active});
  Check(resolve(new[]{"only"},null)=="only","Unique compatible preset selected");
  Check(resolve(new[]{"one","two"},null)==null,"Ambiguous presets need selection");
  Check(resolve(new[]{"one","two"},"two")=="two","Applied preset is fallback");
  typeof(LibraryBindings).GetMethod("SetDefaultPreset").Invoke(b,new object[]{"screen","one"});
  Check(resolve(new[]{"one","two"},"two")=="one","Display default precedes applied");
  b.SetPreset("art","screen","two");Check(resolve(new[]{"one","two"},"one")=="two","Explicit compatible choice wins");
  Check(resolve(new[]{"one"},"two")=="one","Invalid explicit choice falls back");
  Check(resolve(new string[0],"two")==null,"No incompatible preset selected");
  var reload=new LibraryBindings(k=>memory.ContainsKey(k)?memory[k]:null,(k,v)=>memory[k]=v);
  Check((string)method.Invoke(reload,new object[]{"new","screen",new[]{"one","two"},null})=="one","Default persists for new artwork");
  Check((string)method.Invoke(reload,new object[]{"new","other-screen",new[]{"one","two"},null})==null,"Default isolated per display");
 }
 static async Task Run(){
  Check(ArtworkCatalog.NoWallpaper.Id=="no-wallpaper"&&ArtworkCatalog.NoWallpaper.Name=="无壁纸"&&!ArtworkCatalog.All.Contains(ArtworkCatalog.NoWallpaper),"No-wallpaper control must share artwork metadata without entering artwork count");
  foreach(int n in new[]{0,5,60,660}){var page=new LibraryPaging(n);Check(page.PageCount==Math.Max(1,(n+10)/11),"Wrong page capacity");Check(page.Jump(page.PageCount),"Cannot jump last");Check(page.Count==(n==0?0:n-(page.PageCount-1)*11),"Last page count");int previous=page.Page;Check(!page.Jump(0)&&!page.Jump(page.PageCount+1)&&page.Page==previous,"Invalid jump changed page");page.SetTotal(1);Check(page.Page==1&&page.First==0,"Filter did not clamp page");}
  Check(LibraryPaging.PageTokens(30,60).SequenceEqual(new[]{1,0,29,30,31,0,60}),"Missing ellipses");
  Check(LibraryPaging.PageTokens(3,6).SequenceEqual(new[]{1,2,3,4,5,6}),"Short pages hidden");
  Check(LibraryPaging.PageTokens(1,60).SequenceEqual(new[]{1,2,0,60}),"Start pages wrong");
  Check(LibraryPaging.PageTokens(60,60).SequenceEqual(new[]{1,0,59,60}),"End pages wrong");
  Check(new[]{300f,456f,692f,928f,3000f}.Select(LibraryPaging.Columns).SequenceEqual(new[]{1,2,3,4,4}),"Column breakpoints");
  var data=new Dictionary<string,string>();var bindings=new LibraryBindings(k=>data.TryGetValue(k,out var v)?v:null,(k,v)=>data[k]=v);
  bindings.SetPreset("pool","display-a","preset-a");bindings.SetPreset("pool","display-b","preset-b");bindings.SetPreset("train","display-a","preset-c");
  Check(bindings.GetPreset("pool","display-a")=="preset-a"&&bindings.GetPreset("pool","display-b")=="preset-b"&&bindings.GetPreset("train","display-a")=="preset-c","Bindings leaked across pairs");
  Check(bindings.GetPreset("pool/display-a","x")==null,"Composite key collision");
  bindings.SetTarget("showcase","display-b");Check(bindings.ResolveTarget("showcase","display-a")=="display-b","Startup overwrote saved target");int saved=data.Count;Check(bindings.ResolveTarget("new-art","display-a")=="display-a"&&data.Count==saved,"Default initialization persisted target");
  var reloaded=new LibraryBindings(k=>data.TryGetValue(k,out var v)?v:null,(k,v)=>data[k]=v);Check(reloaded.GetPreset("pool","display-b")=="preset-b","Binding reload failed");
  var host=new Host();var action=new WallpaperApplyAction(host);var request=new WallpaperRequest("train","display-a","preset-c",4);
  var pending=action.ApplyAsync(request);Check(action.Busy&&!action.Succeeded&&host.ActiveArtworkId=="pool","Premature apply success");await action.ApplyAsync(request);Check(host.Calls==1,"Duplicate apply");
  host.Pending.SetResult(new WallpaperResult(false,"加载失败"));await pending;Check(!action.Busy&&!action.Succeeded&&action.Message=="加载失败"&&host.ActiveArtworkId=="pool","Failure lost old state");
  host.Pending=new TaskCompletionSource<WallpaperResult>();pending=action.ApplyAsync(request);host.Pending.SetResult(new WallpaperResult(true,"完成"));await pending;Check(action.Succeeded&&host.Request.CalibrationPresetId=="preset-c"&&host.Request.CalibrationPresetRevision==4,"Preset missing from acknowledged request");
  var unavailable=new WallpaperApplyAction(new UnavailableWallpaperHost());await unavailable.ApplyAsync(request);Check(!unavailable.Succeeded&&!unavailable.Busy,"Unavailable host applied");
  Console.WriteLine("PASS paging 0/5/60/660, direct jumps/ellipses, 1-4 columns, independent persistent preset bindings, apply pending/failure/retry/ack/unavailable/duplicate");
 }
}

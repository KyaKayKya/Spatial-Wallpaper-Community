using System;
using System.Linq;
using System.Collections.Generic;
using SpatialWallpaper.Library;
class MainManagerContentChecks {
 static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Main(){
  foreach(var size in new[]{new[]{708,330},new[]{1920,1016},new[]{1920,1080},new[]{3840,2160},new[]{1688,731},new[]{1080,1920}}){SpatialWallpaper.Content.ContentRenderSize.Choose(size[0],size[1],out var width,out var height);Need(width>0&&height>0&&width<=1920&&height<=1920&&(long)width*height<=2073600,"bounded Worker render size");Need(Math.Abs((double)width/height-(double)size[0]/size[1])<.0001,"physical viewport aspect preserved");if((long)size[0]*size[1]<=2073600&&size[0]<=1920&&size[1]<=1920)Need(width==size[0]&&height==size[1],"native resolution must retain detail");}
  bool rejected=false;try{SpatialWallpaper.Content.ContentRenderSize.Choose(0,1080,out var w,out var h);}catch(ArgumentOutOfRangeException){rejected=true;}Need(rejected,"empty target rejected");
  var work=Artwork.FromContent("41cbcf09-5c26-4b41-b939-8a0ffae2b359","SDK train","d01acb12-c42d-4b19-a6d0-5df7d8524782",new string('a',64),"train");
  var blueprint=new Artwork("saved-blueprint","Blueprint","创作","saved","blueprint");
  ArtworkCatalog.Additional=()=>new[]{blueprint};ArtworkCatalog.ResolveAdditional=id=>id==blueprint.Id?blueprint:null;
  ArtworkCatalog.Content=()=>new[]{work};
  Need(work.IsContent&&work.ScenePath==null,"SDK must never be sent to SceneManager");
  Need(work.CoverId=="train"&&work.RevisionId!=null&&work.BuildId.Length==64,"Keep explicit content identity and cover");
  Need(ArtworkCatalog.Find(work.Id)==work,"Resolve GUID content identity");
  Need(ArtworkCatalog.Find(blueprint.Id)==blueprint,"Preserve blueprint resolver");
  Need(ArtworkCatalog.All.Contains(blueprint)&&ArtworkCatalog.All.Contains(work),"Combine catalog sources");
  Need(ArtworkCatalog.All[2].Id=="train"&&!ArtworkCatalog.All[2].IsContent,"Installed SDK must not replace the original train card");
  Need(ArtworkCatalog.Builtins.All(original=>ArtworkCatalog.All.Contains(original)),"Every original remains independently selectable with SDK installed");
  Need(ArtworkCatalog.All.Select(item=>item.Id).Distinct().Count()==ArtworkCatalog.All.Count,"Sources keep distinct work identities");
  Need(ArtworkCatalog.Find("train").Id=="train"&&ArtworkCatalog.Find(work.Id).RevisionId==work.RevisionId,"Resolving original must not redirect an applied exact SDK revision");
  Need(ArtworkCatalog.Builtins.Count==8,"Keep original builtins for fallback");
  Need(ArtworkCatalog.Builtins.All(original=>original.Source==ArtworkSource.BuiltinNative),"Original scenes explicitly use native rendering");
  Need(work.Source==ArtworkSource.SdkLegacy,"Old SDK package must not be relabelled native before migration");
  ArtworkCatalog.Content=()=>Array.Empty<Artwork>();
  Need(ArtworkCatalog.All.Take(8).SequenceEqual(ArtworkCatalog.Builtins),"Removing SDK preserves original identities and order");
  ArtworkCatalog.Content=()=>new[]{work};
  var values=new Dictionary<string,string>();var bindings=new LibraryBindings(k=>values.TryGetValue(k,out var value)?value:null,(k,v)=>values[k]=v);
  bindings.SetTarget("train","display-1");bindings.SetPreset("train","display-1","old-preset");bindings.Migrate("train",work.Id,new[]{"display-1","display-2"});
  Need(bindings.ResolveTarget(work.Id,null)=="display-1"&&bindings.GetPreset(work.Id,"display-1")=="old-preset","Migrate original display and calibration binding");
  bindings.SetPreset(work.Id,"display-1","new-preset");bindings.Migrate("train",work.Id,new[]{"display-1"});Need(bindings.GetPreset(work.Id,"display-1")=="new-preset","Repeated migration must preserve user choice");
  Console.WriteLine("PASS main catalog content GUID, revisions, scene boundary and blueprint coexistence");
 }
}

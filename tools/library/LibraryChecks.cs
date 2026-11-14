using System;
using System.Linq;
using SpatialWallpaper.Library;
class LibraryChecks {
 static void Main(){
  var entries=ArtworkCatalog.All;
  if(entries.Count!=8||entries.Select(x=>x.Id).Distinct().Count()!=8)throw new Exception("Catalog must include eight unique artworks, including Mita");
  string[] names={"白色展示柜","池核","复古车厢","阈限空间","先行者","Limpool","雨夜咖啡馆","米塔"};
  if(!entries.Select(x=>x.Name).SequenceEqual(names))throw new Exception("Public names");
  foreach(var entry in entries)if(string.IsNullOrWhiteSpace(entry.ScenePath)||string.IsNullOrWhiteSpace(entry.Description))throw new Exception("Missing content");
  var host=new UnavailableWallpaperHost();
  if(host.Available||host.ActiveArtworkId!=null||host.ApplyAsync(new WallpaperRequest(entries[0].Id,"display-1")).GetAwaiter().GetResult().Succeeded)throw new Exception("Unavailable host reported success");
  foreach(double aspect in new[]{16.0/9,16.0/10,4.0/3,21.0/9,9.0/16}){
   double width=.53,height=width/aspect;
   if(Math.Abs(ArtworkFraming.Scale(width,height,true)-height/.298125)>1e-9)throw new Exception("Keep height");
   if(Math.Abs(ArtworkFraming.Scale(width,height,false)-width/.53)>1e-9)throw new Exception("Keep width");
  }
  Console.WriteLine("PASS catalog identities/names/paths and unavailable desktop boundary");
 }
}

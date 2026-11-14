using System;
using System.Reflection;
using System.Threading;
using SpatialWallpaper.Blueprint;
static class LayoutChecks {
 static int passed,failed;
 static BlueprintPlacement Fit(BlueprintDocument d)=>BlueprintLayout.Fit(d);
 static void Check(string name,Action run){try{run();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
 static void Near(float a,float b){if(Math.Abs(a-b)>.00001)throw new Exception("expected "+b+", got "+a);}
 static BlueprintDocument Doc(string facing=null){var r=Fixtures.Lite();if(facing!=null){var palette=(fNbt.NbtList)r["Regions"]["a"]["BlockStatePalette"];palette[1]=Fixtures.State("minecraft:cherry_door","facing",facing);}return BlueprintReader.Read(Fixtures.Encode(r),"fixture.litematic",CancellationToken.None);}
 static int Main(){
  Check("empty-blueprint-refused-before-placement",()=>{try{Fit(new BlueprintDocument(3,3,3));throw new Exception("empty source accepted");}catch(BlueprintImportException e){if(e.Code!="empty-blueprint")throw;}});
  Check("legacy-settings-retain-full-selection-layout",()=>{var settings=BlueprintSettings.ReadJson("{\"Version\":1}");if(settings.LayoutVersion!=1||new BlueprintSettings().LayoutVersion!=2)throw new Exception("layout version default");var d=new BlueprintDocument(161,64,129);d.Put(120,40,92,d.Intern(new BlockState("minecraft:oak_planks")));d.Finish();var p=BlueprintLayout.Fit(d,settings.LayoutVersion);Near(p.Scale,.8f/161);Near(p.CenterX,80.5f);Near(p.CenterZ,64.5f);Near(p.MinY,0);if(p.Width!=161||p.Height!=64||p.Length!=129)throw new Exception("legacy footprint changed");if(BlueprintSettings.ReadJson(new BlueprintSettings().ToJson()).LayoutVersion!=2)throw new Exception("new layout roundtrip");});
  Check("padding-does-not-change-visible-fit",()=>{var compact=new BlueprintDocument(9,7,9);var padded=new BlueprintDocument(161,64,129);int a=compact.Intern(new BlockState("minecraft:oak_planks")),b=padded.Intern(new BlockState("minecraft:oak_planks"));compact.Put(0,0,0,a);compact.Put(8,6,8,a);padded.Put(120,40,92,b);padded.Put(128,46,100,b);compact.Finish();padded.Finish();dynamic x=Fit(compact),y=Fit(padded);Near((float)x.Scale,(float)y.Scale);Near((float)x.Z,(float)y.Z);});
  Check("authored-floor-is-aperture-bottom",()=>{dynamic p=Fit(Doc());Near((float)p.GroundY,-.298125f/2);Near((float)p.Y,-.298125f/2);});
  Check("one-uniform-block-scale",()=>{dynamic p=Fit(Doc());float scale=p.Scale;if(scale<=0||scale>.1f)throw new Exception("invalid uniform scale");});
  Check("default-building-stays-behind-aperture",()=>{dynamic p=Fit(Doc());if(p.Z-Math.Sqrt(3*3+3*3)*p.Scale/2<=0)throw new Exception("building crosses aperture");});
  Check("door-orientation-suggests-front",()=>{dynamic p=Fit(Doc("east"));Near((float)p.Yaw,90);p=Fit(Doc("south"));Near((float)p.Yaw,180);});
  Check("orientation-does-not-change-scale-or-centre",()=>{dynamic a=Fit(Doc("east")),b=Fit(Doc("south"));Near((float)a.Scale,(float)b.Scale);Near((float)a.Z,(float)b.Z);});
  Console.WriteLine("Blueprint layout: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}

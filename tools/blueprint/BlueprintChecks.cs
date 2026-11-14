using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using fNbt;

using SpatialWallpaper.Blueprint;
static class BlueprintChecks {
 static int passed,failed;
 static void Eq(object actual,object expected){if(!Equals(actual,expected))throw new Exception("expected "+expected+", got "+actual);}
 static void Check(string name,Action run){try{run();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
 static dynamic Read(byte[] data,string name="fixture.litematic",CancellationToken token=default(CancellationToken)){
  var type=Assembly.GetExecutingAssembly().GetType("SpatialWallpaper.Blueprint.BlueprintReader");if(type==null)throw new Exception("BlueprintReader is not implemented");
  try{return type.GetMethod("Read",new[]{typeof(byte[]),typeof(string),typeof(CancellationToken)}).Invoke(null,new object[]{data,name,token});}catch(TargetInvocationException e){throw e.InnerException;}
 }
 static void Reject(Action run,string code){try{run();}catch(Exception e){if(e.GetBaseException().Message.Contains(code))return;throw new Exception("expected rejection "+code+", got "+e.GetBaseException().Message);}throw new Exception("expected rejection "+code);}
 public static int Main(string[] args){
  Check("invisible-editor-helper-blocks-do-not-render",()=>{if(!new BlockState("minecraft:barrier").IsAir||!new BlockState("minecraft:light").IsAir||new BlockState("minecraft:glass").IsAir)throw new Exception("invisible helpers must be render-air; visible glass must remain");});
  Check("litematic-v6-negative-axis",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Lite()));Eq((int)d.Width,3);Eq((int)d.Height,1);Eq((int)d.Length,3);Eq((int)d.BlockCount,6);Eq((string)d.At(1,0,0).Name,"minecraft:oak_planks");Eq((string)d.At(2,0,0).Property("facing",""),"north");});
  Check("litematic-multiple-region-offsets",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Lite(Fixtures.Region("a",-2,0,1,1,new[]{1}),Fixtures.Region("b",2,0,1,1,new[]{2}))));Eq((int)d.Width,5);Eq((string)d.At(0,0,0).Name,"minecraft:oak_planks");Eq((string)d.At(4,0,0).Name,"minecraft:oak_stairs");Eq((string)d.At(1,0,0).Name,"minecraft:air");});
  Check("litematic-cross-long-bitpacking",()=>{var palette=Enumerable.Range(0,17).Select(i=>Fixtures.State("fixture:block"+i)).ToArray();var values=new int[20];values[12]=16;values[13]=15;dynamic d=Read(Fixtures.Encode(Fixtures.Lite(Fixtures.Region("a",0,0,20,1,values,palette))));Eq((string)d.At(12,0,0).Name,"fixture:block16");Eq((string)d.At(13,0,0).Name,"fixture:block15");});
  Check("litematic-conflicting-overlap-refused",()=>Reject(()=>Read(Fixtures.Encode(Fixtures.Lite(Fixtures.Region("a",0,0,1,1,new[]{1}),Fixtures.Region("b",0,0,1,1,new[]{2})))),"overlapping-regions"));
  Check("litematic-equal-overlap-deduplicated",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Lite(Fixtures.Region("a",0,0,1,1,new[]{1}),Fixtures.Region("b",0,0,1,1,new[]{1}))));Eq((int)d.BlockCount,1);});
  Check("litematic-future-format-refused",()=>{var r=Fixtures.Lite();((NbtInt)r["Version"]).Value=99;Reject(()=>Read(Fixtures.Encode(r)),"unsupported-litematic-version");});
  Check("litematic-zero-dimension-refused",()=>{var r=Fixtures.Lite();((NbtInt)r["Regions"]["a"]["Size"]["x"]).Value=0;Reject(()=>Read(Fixtures.Encode(r)),"dimensions");});
  Check("litematic-huge-bounds-refused",()=>{var r=Fixtures.Lite(Fixtures.Region("a",0,0,1,1),Fixtures.Region("b",int.MaxValue-1,0,1,1));Reject(()=>Read(Fixtures.Encode(r)),"dimensions");});
  Check("litematic-truncated-bit-array-refused",()=>{var r=Fixtures.Lite();((NbtLongArray)r["Regions"]["a"]["BlockStates"]).Value=new long[0];Reject(()=>Read(Fixtures.Encode(r)),"block-data-length");});
  foreach(int version in new[]{2,3}){int v=version;Check("sponge-v"+v+"-states",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Sponge(v)),"fixture.schem");Eq((int)d.Width,2);Eq((int)d.BlockCount,1);Eq((string)d.At(1,0,0).Property("half",""),"top");Eq((string)d.At(1,0,0).Property("facing",""),"east");});}
  Check("sponge-sparse-varint-palette-id",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Sponge(3,300)),"fixture.schem");Eq((string)d.At(1,0,0).Name,"minecraft:oak_stairs");});
  Check("sponge-unknown-palette-id-refused",()=>{var r=Fixtures.Sponge(2);((NbtByteArray)r["BlockData"]).Value=new byte[]{0,2};Reject(()=>Read(Fixtures.Encode(r),"fixture.schem"),"palette-index");});
  Check("sponge-truncated-varint-refused",()=>{var r=Fixtures.Sponge(2);((NbtByteArray)r["BlockData"]).Value=new byte[]{0,128};Reject(()=>Read(Fixtures.Encode(r),"fixture.schem"),"block-data-length");});
  Check("sponge-extra-entries-refused",()=>{var r=Fixtures.Sponge(2);((NbtByteArray)r["BlockData"]).Value=new byte[]{0,1,0};Reject(()=>Read(Fixtures.Encode(r),"fixture.schem"),"block-data-length");});
  Check("vanilla-structure-sparse-blocks",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Structure()),"fixture.nbt");Eq((int)d.Width,2);Eq((int)d.Length,2);Eq((int)d.BlockCount,1);Eq((string)d.At(1,0,1).Name,"minecraft:stone");Eq((string)d.At(0,0,0).Name,"minecraft:air");});
  Check("vanilla-structure-position-bounds",()=>{var r=Fixtures.Structure();((NbtInt)r["blocks"][0]["pos"][0]).Value=4;Reject(()=>Read(Fixtures.Encode(r),"fixture.nbt"),"block-position");});
  Check("legacy-numeric-data-migration",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Legacy()),"fixture.schematic");Eq((string)d.At(0,0,0).Name,"minecraft:spruce_planks");Eq((string)d.At(1,0,0).Name,"minecraft:oak_stairs");Eq((string)d.At(1,0,0).Property("facing",""),"west");Eq((string)d.At(1,0,0).Property("half",""),"top");});
  Check("legacy-unmapped-block-preserved",()=>{var r=Fixtures.Legacy();r["Blocks"].ByteArrayValue[0]=255;dynamic d=Read(Fixtures.Encode(r),"fixture.schematic");Eq((string)d.At(0,0,0).Name,"legacy:block_255");});
  Check("zip-direct-import",()=>{dynamic d=Read(Fixtures.Zip(Fixtures.Encode(Fixtures.Lite())),"fixture.zip");Eq((int)d.BlockCount,6);});
  Check("zip-ambiguous-input-refused",()=>Reject(()=>Read(Fixtures.Zip(Fixtures.Encode(Fixtures.Lite()),Fixtures.Encode(Fixtures.Lite())),"fixture.zip"),"multiple-blueprints"));
  Check("raw-nbt-supported",()=>{dynamic d=Read(Fixtures.Encode(Fixtures.Lite(),false));Eq((int)d.BlockCount,6);});
  Check("cancel-before-import",()=>{var c=new CancellationTokenSource();c.Cancel();Reject(()=>Read(Fixtures.Encode(Fixtures.Lite()),"fixture.litematic",c.Token),"canceled");});
  Check("malformed-input-refused",()=>Reject(()=>Read(new byte[]{1,2,3}),"invalid-nbt"));
  Check("unknown-nbt-tag-refused-as-import-error",()=>Reject(()=>Read(new byte[]{10,0,0,127,0,0,0}),"invalid-nbt"));
  Check("future-minecraft-version-refused",()=>{var r=Fixtures.Lite();((NbtInt)r["MinecraftDataVersion"]).Value=999999;Reject(()=>Read(Fixtures.Encode(r)),"unsupported-data-version");});
  Check("oversized-tag-refused-before-allocation",()=>Reject(()=>Read(new byte[]{10,0,0,7,0,1,65,127,255,255,255,0}),"nbt-budget"));
  Check("entity-data-reported-without-execution",()=>{var r=Fixtures.Structure();((NbtList)r["entities"]).Add(new NbtCompound{new NbtString("id","minecraft:pig")});dynamic d=Read(Fixtures.Encode(r),"fixture.nbt");Eq((int)d.EntityCount,1);});
  if(args.Length>0&&File.Exists(args[0]))Check("user-blueprint-real-file",()=>{dynamic d=Read(File.ReadAllBytes(args[0]),"user.zip");Eq((int)d.Width,25);Eq((int)d.Height,15);Eq((int)d.Length,21);Eq((int)d.BlockCount,2090);Console.WriteLine("User sample: "+d.Palette.Count+" canonical states, "+d.SourceHash);});
  Console.WriteLine("Blueprint core: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}

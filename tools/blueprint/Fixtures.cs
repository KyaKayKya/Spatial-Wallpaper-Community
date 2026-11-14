using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using fNbt;

static class Fixtures {
 public static NbtCompound Vec(string name,int x,int y,int z)=>new NbtCompound(name){new NbtInt("x",x),new NbtInt("y",y),new NbtInt("z",z)};
 public static NbtCompound State(string name,string key=null,string value=null){var c=new NbtCompound{new NbtString("Name",name)};if(key!=null)c.Add(new NbtCompound("Properties"){new NbtString(key,value)});return c;}
 public static byte[] Encode(NbtCompound c,bool compressed=true)=>new NbtFile(c).SaveToBuffer(compressed?NbtCompression.GZip:NbtCompression.None);
 public static NbtCompound Region(string name,int x,int z,int width,int signedLength,int[] values=null,NbtCompound[] palette=null){
  palette=palette??new[]{State("minecraft:air"),State("minecraft:oak_planks"),State("minecraft:oak_stairs","facing","north")};
  int volume=width*Math.Abs(signedLength),bits=Math.Max(2,(int)Math.Ceiling(Math.Log(palette.Length,2)));var words=new long[(volume*bits+63)/64];
  for(int i=0;i<volume;i++){ulong value=(ulong)(values==null?i%palette.Length:values[i]);int b=i*bits;words[b/64]|=unchecked((long)(value<<(b%64)));if(b%64+bits>64)words[b/64+1]|=(long)(value>>(64-b%64));}
  return new NbtCompound(name){Vec("Position",x,0,z),Vec("Size",width,1,signedLength),new NbtList("BlockStatePalette",palette),new NbtLongArray("BlockStates",words),new NbtList("Entities",NbtTagType.Compound),new NbtList("TileEntities",NbtTagType.Compound)};
 }
 public static NbtCompound Lite(params NbtCompound[] regions)=>new NbtCompound(""){new NbtInt("Version",6),new NbtInt("SubVersion",1),new NbtInt("MinecraftDataVersion",3465),new NbtCompound("Metadata"){new NbtString("Name","fixture")},new NbtCompound("Regions",regions.Length==0?new[]{Region("a",0,2,3,-3)}:regions)};
 public static NbtCompound Sponge(int version,int paletteId=1){
  var palette=new NbtCompound("Palette"){new NbtInt("minecraft:air",0),new NbtInt("minecraft:oak_stairs[facing=east,half=top,shape=straight]",paletteId)};
  var bytes=new System.Collections.Generic.List<byte>{0};uint v=(uint)paletteId;do{byte b=(byte)(v&127);v>>=7;if(v>0)b|=128;bytes.Add(b);}while(v>0);
  var root=new NbtCompound(version==2?"Schematic":""){new NbtInt("Version",version),new NbtInt("DataVersion",3465),new NbtShort("Width",2),new NbtShort("Height",1),new NbtShort("Length",1),new NbtIntArray("Offset",new[]{-10,4,7})};
  if(version==2){root.Add(palette);root.Add(new NbtByteArray("BlockData",bytes.ToArray()));root.Add(new NbtList("BlockEntities",NbtTagType.Compound));return root;}
  root.Add(new NbtCompound("Blocks"){palette,new NbtByteArray("Data",bytes.ToArray()),new NbtList("BlockEntities",NbtTagType.Compound)});
  root.Name="Schematic";return new NbtCompound(""){root};
 }
 public static NbtCompound Structure()=>new NbtCompound(""){new NbtInt("DataVersion",3465),new NbtList("size",new NbtTag[]{new NbtInt(2),new NbtInt(1),new NbtInt(2)}),new NbtList("palette",new[]{State("minecraft:air"),State("minecraft:stone")}),new NbtList("blocks",new[]{new NbtCompound{new NbtInt("state",1),new NbtList("pos",new NbtTag[]{new NbtInt(1),new NbtInt(0),new NbtInt(1)})}}),new NbtList("entities",NbtTagType.Compound)};
 public static NbtCompound Legacy()=>new NbtCompound("Schematic"){new NbtString("Materials","Alpha"),new NbtShort("Width",2),new NbtShort("Height",1),new NbtShort("Length",1),new NbtByteArray("Blocks",new byte[]{5,53}),new NbtByteArray("Data",new byte[]{1,5}),new NbtList("TileEntities",NbtTagType.Compound),new NbtList("Entities",NbtTagType.Compound)};
 public static byte[] Zip(params byte[][] items){using(var m=new MemoryStream()){using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){for(int i=0;i<items.Length;i++)using(var s=z.CreateEntry("folder/fixture"+i+".litematic").Open())s.Write(items[i],0,items[i].Length);}return m.ToArray();}}
}

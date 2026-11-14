using System;
using System.IO;
using System.Linq;
using System.Threading;
using fNbt;
using SpatialWallpaper.Blueprint;
class CorpusChecks {
 static int Main(string[] args){string output=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]),"Validation","Blueprint","corpus"));Directory.CreateDirectory(output);foreach(int target in new[]{10000,50000,200000}){
  int w=target==10000?32:target==50000?64:112,h=target==10000?20:target==50000?24:32;var data=new byte[w*h*w];int count=0;
  for(int y=0;y<h;y++)for(int z=0;z<w;z++)for(int x=0;x<w;x++){if(count>=target)continue;if(y%8<4||x%16==0||z%16==0){byte material=(byte)(y%8==0?2:1);if(x%16==0&&y%8>=5)material=3;if(z>w-8&&x>w-8&&y<6)material=4;if(x<8&&z<8&&y>=h-4)material=5;data[x+z*w+y*w*w]=material;count++;}}
  for(int i=0;count<target&&i<data.Length;i++)if(data[i]==0){data[i]=1;count++;}
  var palette=new NbtCompound("Palette");var names=new[]{"minecraft:air","minecraft:stone_bricks","minecraft:oak_planks","minecraft:glass","minecraft:water","minecraft:oak_leaves"};for(int i=0;i<names.Length;i++)palette.Add(new NbtInt(names[i],i));
  var root=new NbtCompound("Schematic"){new NbtInt("Version",2),new NbtInt("DataVersion",3465),new NbtShort("Width",(short)w),new NbtShort("Height",(short)h),new NbtShort("Length",(short)w),palette,new NbtByteArray("BlockData",data),new NbtInt("PaletteMax",names.Length)};
  byte[] bytes=new NbtFile(root).SaveToBuffer(NbtCompression.GZip);string path=Path.Combine(output,"synthetic-"+target+".schem");File.WriteAllBytes(path,bytes);var document=BlueprintReader.Read(bytes,path,CancellationToken.None);if(document.BlockCount!=target)throw new Exception("corpus block count");Console.WriteLine(path+" blocks="+document.BlockCount+" volume="+document.Indices.Length+" bytes="+bytes.Length);
 }return 0;}
}

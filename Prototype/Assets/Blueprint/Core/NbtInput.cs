using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using fNbt;

namespace SpatialWallpaper.Blueprint {
 // Limits apply before fNbt allocates tag payloads or the scene allocates its grid.
 public static class NbtInput {
  public const int MaxInputBytes=32*1024*1024,MaxDecodedBytes=96*1024*1024,MaxVolume=4*1024*1024,MaxAxis=512,MaxPalette=16384,MaxRegions=64,MaxTags=2000000;
  public static void CheckDimensions(long x,long y,long z){if(x<=0||y<=0||z<=0||x>MaxAxis||y>MaxAxis||z>MaxAxis||x*y*z>MaxVolume)throw new BlueprintImportException("dimensions-budget");}
  static byte[] Bounded(Stream stream,int limit,CancellationToken token){using(var output=new MemoryStream()){var buffer=new byte[32768];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(output.Length+count>limit)throw new BlueprintImportException("nbt-budget");output.Write(buffer,0,count);}return output.ToArray();}}
  public static byte[] ReadFile(string path,CancellationToken token){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length>MaxInputBytes)throw new BlueprintImportException("nbt-budget");return Bounded(stream,MaxInputBytes,token);}}
  public static byte[] Decode(byte[] input,CancellationToken token){
   token.ThrowIfCancellationRequested();if(input==null||input.Length==0)throw new BlueprintImportException("invalid-nbt");if(input.Length>MaxInputBytes)throw new BlueprintImportException("nbt-budget");
   if(input.Length>=4&&input[0]==80&&input[1]==75){
    using(var zip=new ZipArchive(new MemoryStream(input,false),ZipArchiveMode.Read)){
     if(zip.Entries.Count>64)throw new BlueprintImportException("nbt-budget");
     var entries=zip.Entries.Where(e=>new[]{".litematic",".schem",".schematic",".nbt"}.Contains(Path.GetExtension(e.FullName).ToLowerInvariant())).ToArray();
     if(entries.Length==0)throw new BlueprintImportException("no-blueprint-in-archive");if(entries.Length!=1)throw new BlueprintImportException("multiple-blueprints");
     if(entries[0].Length>MaxInputBytes)throw new BlueprintImportException("nbt-budget");using(var s=entries[0].Open())input=Bounded(s,MaxInputBytes,token);
    }
   }
   if(input.Length>2&&input[0]==31&&input[1]==139)using(var gzip=new GZipStream(new MemoryStream(input,false),CompressionMode.Decompress))input=Bounded(gzip,MaxDecodedBytes,token);
   if(input.Length<4||input[0]!=10)throw new BlueprintImportException("invalid-nbt");return input;
  }
  public static NbtCompound Read(byte[] bytes,CancellationToken token){
   // Streaming inspection validates declared lengths before the object model reads them.
   using(var stream=new MemoryStream(bytes,false)){
    var reader=new NbtReader(stream);int tags=0;
    while(reader.ReadToFollowing()){
     if((tags++&1023)==0)token.ThrowIfCancellationRequested();
     if(tags>MaxTags||reader.Depth>48||reader.TagLength<0||reader.TagLength>MaxDecodedBytes||stream.Position>stream.Length)throw new BlueprintImportException("nbt-budget");
     int width=reader.TagType==NbtTagType.ByteArray?1:reader.TagType==NbtTagType.IntArray?4:reader.TagType==NbtTagType.LongArray?8:0;
     if(width>0&&(long)reader.TagLength*width>stream.Length-stream.Position)throw new BlueprintImportException("nbt-budget");
     if(reader.TagType==NbtTagType.List&&reader.TagLength>MaxTags)throw new BlueprintImportException("nbt-budget");
    }
    if(stream.Position!=stream.Length)throw new BlueprintImportException("invalid-nbt");
   }
   var file=new NbtFile();long read=file.LoadFromBuffer(bytes,0,bytes.Length,NbtCompression.None);if(read!=bytes.Length)throw new BlueprintImportException("invalid-nbt");return file.RootTag;
  }
 }
}

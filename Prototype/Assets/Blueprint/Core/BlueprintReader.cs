using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using fNbt;

namespace SpatialWallpaper.Blueprint {
 public static class BlueprintReader {
  public static BlueprintDocument Read(byte[] input,string fileName,CancellationToken token){
   try{
    byte[] decoded=NbtInput.Decode(input,token);NbtCompound root=NbtInput.Read(decoded,token);
    if(root["Schematic"] is NbtCompound wrapped)root=wrapped;
    BlueprintDocument document;
    if(root["Regions"] is NbtCompound)document=Lite(root,token);
    else if(root["Version"] is NbtInt&&root["Width"]!=null)document=Sponge(root,token);
    else if(root["Blocks"] is NbtByteArray)document=Legacy(root,token);
    else if(root["size"] is NbtList&&root["blocks"] is NbtList)document=Structure(root,token);
    else throw new BlueprintImportException("unsupported-blueprint-format");
    document.Name=Text((root["Metadata"] as NbtCompound)?["Name"],Path.GetFileNameWithoutExtension(fileName));
    using(var sha=SHA256.Create())document.SourceHash=BitConverter.ToString(sha.ComputeHash(decoded)).Replace("-","").ToLowerInvariant();
    token.ThrowIfCancellationRequested();document.Finish();return document;
   }catch(OperationCanceledException){throw;}catch(BlueprintImportException){throw;}catch(Exception e)when(e is IOException||e is NbtFormatException||e is ArgumentException||e is OverflowException||e is InvalidOperationException||e is IndexOutOfRangeException){throw new BlueprintImportException("invalid-nbt: "+e.Message);}
  }
  static string Text(NbtTag tag,string fallback)=>tag is NbtString s?s.Value:fallback;
  static int Number(NbtTag tag){if(tag is NbtInt i)return i.Value;if(tag is NbtShort s)return unchecked((ushort)s.Value);throw new BlueprintImportException("missing-or-invalid-field");}
  static int Version(NbtCompound c,string key){int version=Number(c[key]);if(version<0||version>3955)throw new BlueprintImportException("unsupported-data-version");return version;}
  static NbtCompound Compound(NbtTag tag)=>tag as NbtCompound??throw new BlueprintImportException("missing-or-invalid-field");
  static NbtList List(NbtTag tag)=>tag as NbtList??throw new BlueprintImportException("missing-or-invalid-field");
  static BlockState State(NbtTag tag){var c=Compound(tag);var props=new Dictionary<string,string>(StringComparer.Ordinal);if(c["Properties"] is NbtCompound properties)foreach(var p in properties){if(!(p is NbtString))throw new BlueprintImportException("invalid-block-state");props.Add(p.Name,p.StringValue);}return new BlockState(Text(c["Name"],null),props);}
  static int[] Palette(BlueprintDocument d,NbtList list){if(list.Count<1||list.Count>NbtInput.MaxPalette)throw new BlueprintImportException("palette-budget");return list.Select(s=>d.Intern(State(s))).ToArray();}
  static int Entities(NbtTag tag)=>tag is NbtList list?list.Count:0;
  sealed class Region {
   public NbtCompound Data;public int Width,Height,Length;public long X,Y,Z;
  }
  static BlueprintDocument Lite(NbtCompound root,CancellationToken token){
   if(Number(root["Version"])!=6)throw new BlueprintImportException("unsupported-litematic-version");int version=Version(root,"MinecraftDataVersion");
   var regions=Compound(root["Regions"]);if(regions.Count==0||regions.Count>NbtInput.MaxRegions)throw new BlueprintImportException("region-budget");
   var areas=new List<Region>();long volume=0;
   foreach(var tag in regions){var c=Compound(tag);var p=Compound(c["Position"]);var s=Compound(c["Size"]);long sx=Number(s["x"]),sy=Number(s["y"]),sz=Number(s["z"]);
    NbtInput.CheckDimensions(Math.Abs(sx),Math.Abs(sy),Math.Abs(sz));volume+=Math.Abs(sx*sy*sz);if(volume>NbtInput.MaxVolume)throw new BlueprintImportException("dimensions-budget");
    areas.Add(new Region{Data=c,Width=(int)Math.Abs(sx),Height=(int)Math.Abs(sy),Length=(int)Math.Abs(sz),X=Number(p["x"])+Math.Min(0,sx+1),Y=Number(p["y"])+Math.Min(0,sy+1),Z=Number(p["z"])+Math.Min(0,sz+1)});
   }
   long minX=areas.Min(a=>a.X),minY=areas.Min(a=>a.Y),minZ=areas.Min(a=>a.Z);
   long width=areas.Max(a=>a.X+a.Width)-minX,height=areas.Max(a=>a.Y+a.Height)-minY,length=areas.Max(a=>a.Z+a.Length)-minZ;NbtInput.CheckDimensions(width,height,length);
   var d=new BlueprintDocument((int)width,(int)height,(int)length){Format="litematic-v6",DataVersion=version,OriginX=minX,OriginY=minY,OriginZ=minZ};
   foreach(var area in areas){token.ThrowIfCancellationRequested();var map=Palette(d,List(area.Data["BlockStatePalette"]));var words=(area.Data["BlockStates"] as NbtLongArray)?.Value??throw new BlueprintImportException("block-data-length");
    int bits=2;while((1<<bits)<map.Length)bits++;int count=area.Width*area.Height*area.Length;if(words.Length!=((long)count*bits+63)/64)throw new BlueprintImportException("block-data-length");ulong mask=(1UL<<bits)-1;
    for(int i=0;i<count;i++){if((i&4095)==0)token.ThrowIfCancellationRequested();long bit=(long)i*bits;int offset=(int)(bit%64),word=(int)(bit/64);ulong value=(ulong)words[word]>>offset;if(offset+bits>64)value|=(ulong)words[word+1]<<(64-offset);int id=(int)(value&mask);if(id>=map.Length)throw new BlueprintImportException("palette-index");
     int x=i%area.Width,z=i/area.Width%area.Length,y=i/(area.Width*area.Length);d.Put((int)(area.X-minX)+x,(int)(area.Y-minY)+y,(int)(area.Z-minZ)+z,map[id],true);
    }
    d.EntityCount+=Entities(area.Data["Entities"]);d.BlockEntityCount+=Entities(area.Data["TileEntities"]);
   }return d;
  }
  static BlueprintDocument Sized(NbtCompound root,string format)=>new BlueprintDocument(Number(root["Width"]),Number(root["Height"]),Number(root["Length"])){Format=format,DataVersion=Version(root,"DataVersion")};
  static BlueprintDocument Sponge(NbtCompound root,CancellationToken token){
   int version=Number(root["Version"]);if(version!=2&&version!=3)throw new BlueprintImportException("unsupported-sponge-version");var d=Sized(root,"sponge-v"+version);
   var container=version==3?Compound(root["Blocks"]):root;var palette=Compound(container["Palette"]);if(palette.Count<1||palette.Count>NbtInput.MaxPalette)throw new BlueprintImportException("palette-budget");
   var map=new Dictionary<int,int>();foreach(var entry in palette){int id=Number(entry);if(id<0||map.ContainsKey(id))throw new BlueprintImportException("palette-index");map.Add(id,d.Intern(BlockState.Parse(entry.Name)));}
   var bytes=(container[version==3?"Data":"BlockData"] as NbtByteArray)?.Value??throw new BlueprintImportException("block-data-length");int pos=0;
   for(int i=0;i<d.Indices.Length;i++){if((i&4095)==0)token.ThrowIfCancellationRequested();uint value=0;int shift=0;while(true){if(pos>=bytes.Length||shift>28)throw new BlueprintImportException("block-data-length");byte b=bytes[pos++];if(shift==28&&(b&248)!=0)throw new BlueprintImportException("palette-index");value|=(uint)(b&127)<<shift;if((b&128)==0)break;shift+=7;}if(!map.TryGetValue((int)value,out int id))throw new BlueprintImportException("palette-index");d.Indices[i]=id;}
   if(pos!=bytes.Length)throw new BlueprintImportException("block-data-length");
   if(root["Offset"] is NbtIntArray offset&&offset.Value.Length==3){d.OriginX=offset.Value[0];d.OriginY=offset.Value[1];d.OriginZ=offset.Value[2];}
   d.EntityCount=Entities(root["Entities"]);d.BlockEntityCount=Entities(container["BlockEntities"]);return d;
  }
  static BlueprintDocument Structure(NbtCompound root,CancellationToken token){
   var size=List(root["size"]);if(size.Count!=3)throw new BlueprintImportException("dimensions");var d=new BlueprintDocument(Number(size[0]),Number(size[1]),Number(size[2])){Format="structure-nbt",DataVersion=Version(root,"DataVersion")};
   var sourcePalette=root["palette"] as NbtList;if(sourcePalette==null&&root["palettes"] is NbtList multiple&&multiple.Count>0)sourcePalette=multiple[0] as NbtList;
   var map=Palette(d,sourcePalette??throw new BlueprintImportException("palette-index"));var blocks=List(root["blocks"]);if(blocks.Count>d.Indices.Length)throw new BlueprintImportException("block-data-length");var occupied=new HashSet<int>();
   foreach(var tag in blocks){if((occupied.Count&4095)==0)token.ThrowIfCancellationRequested();var c=Compound(tag);var p=List(c["pos"]);if(p.Count!=3)throw new BlueprintImportException("block-position");int x=Number(p[0]),y=Number(p[1]),z=Number(p[2]),id=Number(c["state"]);if(id<0||id>=map.Length)throw new BlueprintImportException("palette-index");d.Put(x,y,z,map[id]);if(!occupied.Add(d.Index(x,y,z)))throw new BlueprintImportException("overlapping-regions");if(c["nbt"] is NbtCompound)d.BlockEntityCount++;}
   d.EntityCount=Entities(root["entities"]);return d;
  }
  static BlueprintDocument Legacy(NbtCompound root,CancellationToken token){
   var d=new BlueprintDocument(Number(root["Width"]),Number(root["Height"]),Number(root["Length"])){Format="mcedit-alpha",DataVersion=1343};
   var ids=(root["Blocks"] as NbtByteArray)?.Value;var states=(root["Data"] as NbtByteArray)?.Value;var high=(root["AddBlocks"] as NbtByteArray)?.Value;
   if(ids==null||states==null||ids.Length!=d.Indices.Length||states.Length!=ids.Length||high!=null&&high.Length!=(ids.Length+1)/2)throw new BlueprintImportException("block-data-length");
   for(int i=0;i<ids.Length;i++){if((i&4095)==0)token.ThrowIfCancellationRequested();int id=ids[i]+(high==null?0:((high[i/2]>>((i%2)*4))&15)*256);d.Indices[i]=d.Intern(LegacyBlockStates.Resolve(id,states[i]&15));}
   d.EntityCount=Entities(root["Entities"]);d.BlockEntityCount=Entities(root["TileEntities"]);return d;
  }
 }
}

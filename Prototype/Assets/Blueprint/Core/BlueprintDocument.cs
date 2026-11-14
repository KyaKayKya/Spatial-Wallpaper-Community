using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintImportException:IOException {
  public string Code {get;}
  public BlueprintImportException(string code):base(code){Code=code;}
 }
 [Serializable] public sealed class BlockState {
  static readonly Regex Identifier=new Regex(@"^[a-z0-9_.-]+:[a-z0-9_./-]+$",RegexOptions.CultureInvariant);
  public string Name {get;}
  public IReadOnlyDictionary<string,string> Properties {get;}
  public string Key {get;}
  public bool IsAir=>Name=="minecraft:air"||Name=="minecraft:cave_air"||Name=="minecraft:void_air"||Name=="minecraft:structure_void"||Name=="minecraft:barrier"||Name=="minecraft:light";
  public BlockState(string name,IDictionary<string,string> properties=null){
   if(name==null||name.Length>256)throw new BlueprintImportException("invalid-block-state");
   Name=name.Contains(":")?name:"minecraft:"+name;if(!Identifier.IsMatch(Name))throw new BlueprintImportException("invalid-block-state");
   var sorted=new SortedDictionary<string,string>(StringComparer.Ordinal);
   if(properties!=null)foreach(var pair in properties){if(pair.Key.Length==0||pair.Key.Length>64||pair.Value==null||pair.Value.Length>128)throw new BlueprintImportException("invalid-block-state");sorted.Add(pair.Key,pair.Value);}
   if(sorted.Count>32)throw new BlueprintImportException("invalid-block-state");
   Properties=sorted;Key=Name+(sorted.Count==0?"":"["+string.Join(",",sorted.Select(p=>p.Key+"="+p.Value))+"]");
  }
  public string Property(string key,string fallback)=>Properties.TryGetValue(key,out var value)?value:fallback;
  public static BlockState Parse(string value){
   if(value==null||value.Length>4096)throw new BlueprintImportException("invalid-block-state");
   int bracket=value.IndexOf('[');if(bracket<0)return new BlockState(value);
   if(!value.EndsWith("]",StringComparison.Ordinal))throw new BlueprintImportException("invalid-block-state");
   var props=new Dictionary<string,string>(StringComparer.Ordinal);string inner=value.Substring(bracket+1,value.Length-bracket-2);
   if(inner.Length>0)foreach(string item in inner.Split(',')){int eq=item.IndexOf('=');if(eq<=0||eq==item.Length-1||props.ContainsKey(item.Substring(0,eq)))throw new BlueprintImportException("invalid-block-state");props.Add(item.Substring(0,eq),item.Substring(eq+1));}
   return new BlockState(value.Substring(0,bracket),props);
  }
 }
 [Serializable] public sealed class BlueprintDocument {
  public int Width {get;} public int Height {get;} public int Length {get;}
  public int DataVersion {get;internal set;} public string Format {get;internal set;}
  public string Name {get;internal set;} public string SourceHash {get;internal set;}
  public int EntityCount {get;internal set;} public int BlockEntityCount {get;internal set;}
  public int BlockCount {get;internal set;}
  public long OriginX {get;internal set;} public long OriginY {get;internal set;} public long OriginZ {get;internal set;}
  public List<BlockState> Palette {get;}=new List<BlockState>{new BlockState("minecraft:air")};
  public int[] Indices {get;}
  readonly Dictionary<string,int> paletteMap=new Dictionary<string,int>(StringComparer.Ordinal){{"minecraft:air",0}};
  internal BlueprintDocument(int width,int height,int length){NbtInput.CheckDimensions(width,height,length);Width=width;Height=height;Length=length;Indices=new int[width*height*length];}
  internal int Intern(BlockState state){if(state.IsAir)return 0;if(paletteMap.TryGetValue(state.Key,out int id))return id;if(Palette.Count>=NbtInput.MaxPalette)throw new BlueprintImportException("palette-budget");id=Palette.Count;Palette.Add(state);paletteMap.Add(state.Key,id);return id;}
  public int Index(int x,int y,int z)=>x+z*Width+y*Width*Length;
  public BlockState At(int x,int y,int z)=>x<0||y<0||z<0||x>=Width||y>=Height||z>=Length?Palette[0]:Palette[Indices[Index(x,y,z)]];
  internal void Put(int x,int y,int z,int id,bool checkOverlap=false){
   if(x<0||y<0||z<0||x>=Width||y>=Height||z>=Length)throw new BlueprintImportException("block-position");
   int index=Index(x,y,z),old=Indices[index];if(checkOverlap&&old!=0&&id!=0&&old!=id)throw new BlueprintImportException("overlapping-regions");
   if(checkOverlap&&id==0)return;Indices[index]=id;
  }
  internal void Finish(){BlockCount=Indices.Count(x=>x!=0);}
 }
}

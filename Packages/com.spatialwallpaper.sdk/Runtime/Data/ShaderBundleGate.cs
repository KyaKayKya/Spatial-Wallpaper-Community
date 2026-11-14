using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace SpatialWallpaper.Sdk {
 // This gate accepts ONLY Shader + AssetBundle objects with the pinned native
 // layout. Meshes, textures and scene objects use the platform's data formats.
 // Unlike the P1 type-table observation, every object payload is traversed.
 public static class ShaderBundleGate {
  const int Limit=32*1024*1024;
  static void Need(bool value,string message){JsonData.Require(value,"shader-bundle:"+message);}
  public static void Validate(byte[] bytes){Inspect(bytes);}
  public sealed class ShaderDescription { public string Name; public Dictionary<string,int> TextureDimensions=new Dictionary<string,int>(StringComparer.Ordinal); internal readonly List<int> Dimensions=new List<int>(); public Dictionary<string,int> Properties=new Dictionary<string,int>(StringComparer.Ordinal); internal readonly List<string> PropertyNames=new List<string>(); internal readonly List<int> PropertyTypes=new List<int>(); }
  public static ShaderDescription[] Inspect(byte[] bytes){long ignored;return Inspect(bytes,out ignored);}
  public static ShaderDescription[] Inspect(byte[] bytes,out long decodedBytes){
   Need(bytes!=null&&bytes.Length<=Limit,"size");
   var r=new Reader(bytes,true);
   Need(r.ZString()=="UnityFS"&&r.U32()==8,"header");r.ZString();Need(r.ZString()=="2022.3.62f3","engine");
   Need(r.I64()==bytes.Length,"file-length");int packed=r.Count(Limit),unpacked=r.Count(Limit);Need(r.U32()==0x243,"flags");r.Align(16);
   var meta=new Reader(Lz4(r.Bytes(packed),unpacked),true);r.Align(16);meta.Skip(16);
   int blocks=meta.Count(256),length=0;
   for(int i=0;i<blocks;i++){int u=meta.Count(Limit),p=meta.Count(Limit);Need(p==u&&(meta.U16()&63)==0&&u<=Limit-length,"block");length+=u;}
   Need(length==r.Left&&meta.Count(1)==1,"single-entry");var data=r.Bytes(length);
   Need(meta.I64()==0,"entry-offset");long size=meta.I64();Need(size>0&&size<=data.Length&&data.Length-size<16,"entry-size");Need(meta.U32()==4,"serialized-entry");
   string name=meta.ZString();Need(name.StartsWith("CAB-",StringComparison.Ordinal)&&name.Length==36&&name.IndexOfAny(new[]{'/',':','\\'})<0&&meta.Left==0,"entry-name");
   for(int i=(int)size;i<data.Length;i++)Need(data[i]==0,"entry-padding");
   var file=new byte[(int)size];Array.Copy(data,file,file.Length);return Serialized(file,out decodedBytes);
  }
  sealed class Node {public int Level,Size,Flags;public string Type,Name;public List<Node> Children=new List<Node>();}
  sealed class Type {public int Class;public Node Root;}
  sealed class Obj {public long Id;public int Offset,Length,Type;}
  static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static ShaderDescription[] Serialized(byte[] bytes,out long decodedBytes){
   var descriptions=new List<ShaderDescription>();
   var r=new Reader(bytes,true);Need(r.U32()==0&&r.U32()==0&&r.U32()==22&&r.U32()==0,"serialized-header");Need(r.Byte()==0,"endian");r.Zero(3);
   int metadataSize=r.Count(Limit);Need(r.I64()==bytes.Length,"serialized-size");long dataOffset=r.I64();Need(r.I64()==0&&dataOffset>=48+metadataSize&&dataOffset<bytes.Length,"data-offset");
   r.Big=false;Need(r.ZString()=="2022.3.62f3"&&r.U32()==19&&r.Byte()==1,"target-or-tree");
   int count=r.Count(2);Need(count==2,"types");var types=new List<Type>();var classes=new HashSet<int>();
   for(int t=0;t<count;t++){
    int id=(int)r.U32();Need((id==48||id==142)&&classes.Add(id),"class");Need(r.Byte()==0&&r.U16()==65535,"script-type");r.Skip(16);
    int start=r.Position,n=r.Count(4096),strings=r.Count(16384);var raw=r.Bytes(n*32);var buffer=r.Bytes(strings);
    var treeBytes=new byte[r.Position-start];Array.Copy(bytes,start,treeBytes,0,treeBytes.Length);
    Need(Hash(treeBytes)==(id==48?"0a13a2739e481919358825c29f1649d2ab56d156ca4c58089b1f8ca392909b92":"db1260462eea0c3f9c186b03350dcce31db41442e711aad844cd9214688cda71"),"unrecognized-type-layout");
    Need(r.Count(0)==0,"type-dependency");
    var nr=new Reader(raw,false);var stack=new Stack<Node>();Node root=null;
    for(int j=0;j<n;j++){
     nr.U16();int level=nr.Byte();nr.Byte();uint ty=nr.U32(),na=nr.U32();int sz=(int)nr.U32();nr.U32();int flags=(int)nr.U32();nr.I64();
     var node=new Node{Level=level,Size=sz,Flags=flags,Type=StringAt(buffer,ty),Name=StringAt(buffer,na)};
     while(stack.Count>0&&stack.Peek().Level>=level)stack.Pop();
     if(stack.Count==0){Need(root==null&&level==0,"tree-root");root=node;}else stack.Peek().Children.Add(node);stack.Push(node);
    }
    types.Add(new Type{Class=id,Root=root});
   }
   int objects=r.Count(257);Need(objects>=2,"object-count");var entries=new List<Obj>();var shaders=new HashSet<long>();var ids=new HashSet<long>();int bundleCount=0;
   for(int i=0;i<objects;i++){
    r.Align(4);long id=r.I64(),offset=r.I64();int len=r.Count(Limit),type=r.Count(count-1);
    Need(id!=0&&ids.Add(id)&&offset>=0&&offset<=bytes.Length-dataOffset&&len<=bytes.Length-dataOffset-offset,"object-range");
    Need(offset%8==0,"object-alignment");entries.Add(new Obj{Id=id,Offset=(int)(dataOffset+offset),Length=len,Type=type});
    if(types[type].Class==48)shaders.Add(id);else bundleCount++;
   }
   Need(bundleCount==1&&shaders.Count>0,"object-classes");
   Need(r.Count(0)==0&&r.Count(0)==0&&r.Count(0)==0&&r.ZString()=="","external-or-script-reference");
   Need(r.Position==48+metadataSize,"metadata-end");r.Zero((int)dataOffset-r.Position);
   entries.Sort((a,b)=>a.Offset.CompareTo(b.Offset));int previous=(int)dataOffset;
   long programBudget=0,parameterBudget=0;int traversalBudget=4000000;var dependencies=new Dictionary<long,HashSet<long>>();
   foreach(var entry in entries){
    Need(entry.Offset>=previous,"overlapping-objects");for(int i=previous;i<entry.Offset;i++)Need(bytes[i]==0,"object-padding");
    var objectBytes=new byte[entry.Length];Array.Copy(bytes,entry.Offset,objectBytes,0,entry.Length);var payload=new Reader(objectBytes,false);
    bool isShader=types[entry.Type].Class==48;var references=new HashSet<long>();var fields=new Dictionary<string,Tuple<int,int>>();var programs=new HashSet<int>();var parameters=new HashSet<int>();var description=new ShaderDescription();Walk(types[entry.Type].Root,payload,shaders,ref traversalBudget,fields,programs,parameters,references,isShader,false,false,isShader?description:null);Need(payload.Left==0,"object-tail");
    if(isShader){Programs(objectBytes,fields,ref programBudget,ref parameterBudget,programs,parameters);dependencies.Add(entry.Id,references);
     Need(!string.IsNullOrEmpty(description.Name)&&description.PropertyNames.Count==description.PropertyTypes.Count&&description.PropertyNames.Count==description.Dimensions.Count,"shader-description");
     for(int j=0;j<description.PropertyNames.Count;j++){Need(!description.Properties.ContainsKey(description.PropertyNames[j]),"duplicate-shader-property");description.Properties.Add(description.PropertyNames[j],description.PropertyTypes[j]);description.TextureDimensions.Add(description.PropertyNames[j],description.Dimensions[j]);}
     Need(!descriptions.Any(d=>d.Name==description.Name),"duplicate-shader-name");descriptions.Add(description);}previous=entry.Offset+entry.Length;
   }
   for(int i=previous;i<bytes.Length;i++)Need(bytes[i]==0,"file-padding");
   var visited=new HashSet<long>();var visiting=new HashSet<long>();foreach(long id in shaders)Visit(id,dependencies,visited,visiting);decodedBytes=checked(bytes.Length+programBudget+parameterBudget);return descriptions.ToArray();
  }
  static void Visit(long id,Dictionary<long,HashSet<long>> dependencies,HashSet<long> visited,HashSet<long> visiting){
   if(visited.Contains(id))return;Need(visiting.Add(id),"cyclic-shader-dependency");foreach(long target in dependencies[id])Visit(target,dependencies,visited,visiting);visiting.Remove(id);visited.Add(id);
  }
  static string StringAt(byte[] buffer,uint offset){
   if((offset&0x80000000)!=0)return "@"+(offset&0x7fffffff);
   Need(offset<buffer.Length,"tree-string");int end=(int)offset;while(end<buffer.Length&&buffer[end]!=0)end++;Need(end<buffer.Length,"tree-string-end");return Encoding.UTF8.GetString(buffer,(int)offset,end-(int)offset);
  }
  static void Programs(byte[] bytes,Dictionary<string,Tuple<int,int>> fields,ref long programBudget,ref long parameterBudget,HashSet<int> programIndices,HashSet<int> parameterIndices){
   Func<string,Reader> field=name=>{var span=fields[name];var data=new byte[span.Item2-span.Item1];Array.Copy(bytes,span.Item1,data,0,data.Length);return new Reader(data,false);};
   var platforms=field("platforms");int count=platforms.Count(1);for(int i=0;i<count;i++)Need(platforms.U32()==4,"graphics-platform");Need(count==1&&platforms.Left==0,"shader-has-no-program");
   Func<string,List<List<uint>>> matrix=name=>{
    var r=field(name);Need(r.Count(32)==count,"program-platform-count");var result=new List<List<uint>>();
    for(int i=0;i<count;i++){int n=r.Count(65536);var row=new List<uint>();for(int j=0;j<n;j++)row.Add(r.U32());result.Add(row);}Need(r.Left==0,"program-matrix-tail");return result;
   };
   var offsets=matrix("offsets");var packed=matrix("compressedLengths");var unpacked=matrix("decompressedLengths");
   var blobReader=field("compressedBlob");var blob=blobReader.Bytes(blobReader.Count(Limit));blobReader.Align(4);Need(blobReader.Left==0,"program-blob-tail");
   for(int i=0;i<count;i++){
    var segments=new List<byte[]>();
    Need(offsets[i].Count==packed[i].Count&&offsets[i].Count==unpacked[i].Count,"program-count");
    for(int j=0;j<offsets[i].Count;j++){
     uint offset=offsets[i][j],size=packed[i][j],expanded=unpacked[i][j];Need(offset<=blob.Length&&size<=blob.Length-offset&&expanded<=Limit&&programBudget<=Limit-expanded,"program-range-or-expansion");programBudget+=expanded;
     var source=new byte[(int)size];Array.Copy(blob,(int)offset,source,0,source.Length);segments.Add(Lz4(source,(int)expanded));
    }
    long charge=ShaderProgramData.Validate(segments,programIndices,parameterIndices);Need(charge<=Limit-parameterBudget,"aggregate-parameter-budget");parameterBudget+=charge;
   }
  }
  static void Walk(Node node,Reader r,HashSet<long> shaders,ref int budget,Dictionary<string,Tuple<int,int>> fields,HashSet<int> programs,HashSet<int> parameters,HashSet<long> references,bool isShader,bool player=false,bool parameter=false,ShaderDescription description=null,string propertyPath=""){
   propertyPath+="/"+node.Name;
   int start=r.Position;
   if(node.Name=="m_PlayerSubPrograms")player=true;if(node.Name=="m_ParameterBlobIndices")parameter=true;
   Need(--budget>=0,"traversal-budget");
   if(node.Type.StartsWith("PPtr<",StringComparison.Ordinal)||node.Type=="@633"||node.Type=="@688"){
    int file=(int)r.U32();long path=r.I64();Need(file==0&&(path==0||node.Type!="@688"&&shaders.Contains(path)),"object-reference");if(path!=0&&isShader)references.Add(path);
   } else if(node.Type=="@840"){
    int n=r.Count(1024*1024);var text=r.Bytes(n);string decoded=new UTF8Encoding(false,true).GetString(text);if(description!=null){
     if(propertyPath=="/@55/m_ParsedForm/@427"){Need(decoded.Length>0&&decoded.Length<=1024,"shader-name");description.Name=decoded;}
     if(propertyPath=="/@55/m_ParsedForm/m_PropInfo/m_Props/@49/@106/@427"){Need(decoded.Length>0&&decoded.Length<=128&&description.PropertyNames.Count<1024,"shader-property-name");description.PropertyNames.Add(decoded);}
    }r.Align(4);
   } else if(node.Type=="@49"){
    Need(node.Children.Count==2,"array-layout");int n=r.Count(Limit);
    if(node.Name=="m_Dependencies"&&!isShader||node.Name=="m_NonModifiableTextures"||node.Name=="m_SceneHashes")Need(n==0,"resource-dependency");
    var item=node.Children[1];
    if(item.Children.Count==0&&item.Size==1){Need(n<=r.Left,"byte-array");r.Skip(n);}
    else {Need(n<=r.Left&&n<=1000000,"array-count");for(int i=0;i<n;i++)Walk(item,r,shaders,ref budget,fields,programs,parameters,references,isShader,player,parameter,description,propertyPath);}
   } else if(node.Children.Count>0){
    if(node.Name=="m_Dependencies"&&!isShader||node.Name=="m_NonModifiableTextures"||node.Name=="m_SceneHashes"){
     Need(r.Peek32()==0,"resource-dependency");
    }
    foreach(var child in node.Children)Walk(child,r,shaders,ref budget,fields,programs,parameters,references,isShader,player,parameter,description,propertyPath);
   } else {
    Need(node.Size>=0&&node.Size<=16,"primitive-layout");
    if(description!=null&&propertyPath=="/@55/m_ParsedForm/m_PropInfo/m_Props/@49/@106/m_DefTexture/m_TexDim"){Need(node.Size==4,"shader-texture-dimension-layout");int position=r.Position;description.Dimensions.Add(r.Count(6));r.Position=position;}
    if(description!=null&&propertyPath=="/@55/m_ParsedForm/m_PropInfo/m_Props/@49/@106/@519"){Need(node.Size==4,"shader-property-layout");int position=r.Position;description.PropertyTypes.Add(r.Count(5));r.Position=position;}
    if(node.Name=="m_BlobIndex"&&player||parameter&&node.Size==4){int index=r.Count(65535);if(parameter)parameters.Add(index);else programs.Add(index);}
    else if(node.Type=="@76"){byte v=r.Byte();Need(v<=1&&(node.Name!="m_IsStreamedSceneAssetBundle"||v==0),"bool-or-scene-value");}
    else if(node.Type=="@161"){float v=BitConverter.ToSingle(r.Bytes(4),0);Need(!float.IsNaN(v)&&!float.IsInfinity(v),"float-value");}
    else r.Skip(node.Size);
   }
   if((node.Flags&0x4000)!=0)r.Align(4);
   if(node.Level==1)fields.Add(node.Name,Tuple.Create(start,r.Position));
  }
  static int Extended(Reader r,int v){if(v!=15)return v;int next;do{next=r.Byte();Need(v<=Limit-next,"lz4-length");v+=next;}while(next==255);return v;}
  static byte[] Lz4(byte[] source,int size){
   var r=new Reader(source,false);var target=new byte[size];int p=0;
   while(r.Left>0){int token=r.Byte(),literal=Extended(r,token>>4);Need(literal<=size-p,"lz4-literal");Array.Copy(r.Bytes(literal),0,target,p,literal);p+=literal;if(r.Left==0)break;
    int d=r.U16(),n=Extended(r,token&15)+4;Need(d>0&&d<=p&&n<=size-p,"lz4-match");for(int i=0;i<n;i++){target[p]=target[p-d];p++;}}
   Need(p==size,"lz4-size");return target;
  }
  sealed class Reader {
   readonly byte[] data;public int Position;public bool Big;public int Left=>data.Length-Position;
   public Reader(byte[] bytes,bool big){data=bytes;Big=big;}
   public byte Byte(){Need(Left>0,"truncated");return data[Position++];}
   public ushort U16(){uint a=Byte(),b=Byte();return (ushort)(Big?(a<<8)|b:a|(b<<8));}
   public uint U32(){uint a=U16(),b=U16();return Big?(a<<16)|b:a|(b<<16);}
   public uint Peek32(){int p=Position;uint v=U32();Position=p;return v;}
   public long I64(){ulong a=U32(),b=U32();return (long)(Big?(a<<32)|b:a|(b<<32));}
   public int Count(int max){uint n=U32();Need(n<=max,"count");return (int)n;}
   public void Skip(int n){Need(n>=0&&n<=Left,"truncated");Position+=n;}
   public byte[] Bytes(int n){Need(n>=0&&n<=Left,"truncated");var bytes=new byte[n];Array.Copy(data,Position,bytes,0,n);Position+=n;return bytes;}
   public void Zero(int n){Need(n>=0&&n<=Left,"padding-range");for(int i=0;i<n;i++)Need(Byte()==0,"padding");}
   public void Align(int n){Zero((n-Position%n)%n);}
   public string ZString(){int start=Position;while(Byte()!=0)Need(Position-start<=256,"string");return new UTF8Encoding(false,true).GetString(data,start,Position-start-1);}
  }
 }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SpatialWallpaper.Blueprint {
 [Serializable] public sealed class BlueprintMeshPart {
  public string Name;public BlockRenderKind Kind;
  public float[] Positions,Normals,Uv;public byte[] Colors;public int[] Indices;
 }
 [Serializable] public sealed class CompiledBlueprint {
  public string CacheKey,SourceHash,CatalogFingerprint;public int Width,Height,Length,BlockCount,DataVersion,EntityCount,BlockEntityCount;
  public List<BlueprintMeshPart> Parts {get;}=new List<BlueprintMeshPart>();
  public List<string> MissingStates {get;}=new List<string>();
  public int TriangleCount=>Parts.Sum(p=>p.Indices.Length/3);
  public bool CanApply=>Parts.Count>0&&MissingStates.Count==0&&EntityCount==0;
 }
 public static class BlockMeshCompiler {
  public const string Version="static-block-mesh-6-invisible-helpers";public const int MaxVertices=2000000;
  const int BatchSize=16;
  public static string CacheKeyFor(string sourceHash,string catalogFingerprint){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sourceHash+"|"+catalogFingerprint+"|"+Version))).Replace("-","").ToLowerInvariant();}
  sealed class Builder {
   public string Name;public BlockRenderKind Kind;public List<float> P=new List<float>(),N=new List<float>(),U=new List<float>();public List<byte>C=new List<byte>();public List<int>I=new List<int>();
   public BlueprintMeshPart Finish()=>new BlueprintMeshPart{Name=Name,Kind=Kind,Positions=P.ToArray(),Normals=N.ToArray(),Uv=U.ToArray(),Colors=C.ToArray(),Indices=I.ToArray()};
  }
  public static CompiledBlueprint Compile(BlueprintDocument d,ModelCatalog catalog,CancellationToken token){
   token.ThrowIfCancellationRequested();if(d==null||catalog==null)throw new ArgumentNullException();
   var result=new CompiledBlueprint{SourceHash=d.SourceHash,CatalogFingerprint=catalog.Fingerprint,Width=d.Width,Height=d.Height,Length=d.Length,DataVersion=d.DataVersion,BlockCount=d.BlockCount,EntityCount=d.EntityCount,BlockEntityCount=d.BlockEntityCount};
   result.CacheKey=CacheKeyFor(d.SourceHash,catalog.Fingerprint);
   var models=d.Palette.Select(catalog.Resolve).ToArray();var batches=new SortedDictionary<string,List<Builder>>(StringComparer.Ordinal);var missing=new SortedSet<string>(StringComparer.Ordinal);int vertexCount=0;
   Func<int,int,int,int> stateAt=(x,y,z)=>x<0||y<0||z<0||x>=d.Width||y>=d.Height||z>=d.Length?0:d.Indices[d.Index(x,y,z)];
   Func<int,int,int,bool> solid=(x,y,z)=>models[stateAt(x,y,z)].FullCube;
   for(int y=0;y<d.Height;y++)for(int z=0;z<d.Length;z++)for(int x=0;x<d.Width;x++){
    int index=d.Index(x,y,z);if((index&4095)==0)token.ThrowIfCancellationRequested();int state=d.Indices[index];if(state==0)continue;var model=models[state];if(!model.Supported)missing.Add(d.Palette[state].Key+" — "+model.Problem);
    foreach(var quad in model.Quads){
     if(quad.CullFace>=0){var direction=ShapeGeometry.Directions[quad.CullFace];int other=stateAt(x+(int)direction.X,y+(int)direction.Y,z+(int)direction.Z);
      if(models[other].FullCube)continue;
      if((quad.Kind==BlockRenderKind.Glass||quad.Kind==BlockRenderKind.Water)&&other!=0&&d.Palette[other].Name==d.Palette[state].Name)continue;
     }
     if(vertexCount+4>MaxVertices)throw new BlueprintImportException("mesh-vertex-budget");vertexCount+=4;
     string key=(x/BatchSize)+","+(y/BatchSize)+","+(z/BatchSize)+":"+(int)quad.Kind;
     if(!batches.TryGetValue(key,out var group)){group=new List<Builder>();batches.Add(key,group);}
     if(group.Count==0||group[group.Count-1].P.Count/3+4>60000)group.Add(new Builder{Name=key+"/"+group.Count,Kind=quad.Kind});var b=group[group.Count-1];int first=b.P.Count/3;
     var region=catalog.Texture(quad.Texture);
     for(int i=0;i<4;i++){
      var p=quad.Points[i];var uv=region.Map(quad.Uv[i]);b.P.Add(p.X+x);b.P.Add(p.Y+y);b.P.Add(p.Z+z);b.N.Add(quad.Normal.X);b.N.Add(quad.Normal.Y);b.N.Add(quad.Normal.Z);b.U.Add(uv.X);b.U.Add(uv.Y);
      float ao=Occlusion(x,y,z,p,quad.CullFace,solid);int r=quad.Tint==1?125:quad.Tint==2?113:255,g=quad.Tint==1?164:quad.Tint==2?153:255,blue=quad.Tint==1?75:quad.Tint==2?80:255;
      b.C.Add((byte)(r*ao));b.C.Add((byte)(g*ao));b.C.Add((byte)(blue*ao));b.C.Add(quad.Wind?(byte)(Math.Max(0,Math.Min(1,p.Y))*255):(byte)0);
     }
     foreach(int i in new[]{0,1,2,0,2,3})b.I.Add(first+i);
    }
   }
   foreach(var entry in batches)foreach(var b in entry.Value)result.Parts.Add(b.Finish());result.MissingStates.AddRange(missing);return result;
  }
  static float Occlusion(int x,int y,int z,Float3 p,int face,Func<int,int,int,bool> solid){
   if(face<0)return 1;var n=ShapeGeometry.Directions[face];int ox=x+(int)n.X,oy=y+(int)n.Y,oz=z+(int)n.Z;
   int a,b,c;
   if(face<2){int u=p.Y>.5f?1:-1,v=p.Z>.5f?1:-1;a=solid(ox,oy+u,oz)?1:0;b=solid(ox,oy,oz+v)?1:0;c=solid(ox,oy+u,oz+v)?1:0;}
   else if(face<4){int u=p.X>.5f?1:-1,v=p.Z>.5f?1:-1;a=solid(ox+u,oy,oz)?1:0;b=solid(ox,oy,oz+v)?1:0;c=solid(ox+u,oy,oz+v)?1:0;}
   else{int u=p.X>.5f?1:-1,v=p.Y>.5f?1:-1;a=solid(ox+u,oy,oz)?1:0;b=solid(ox,oy+v,oz)?1:0;c=solid(ox+u,oy+v,oz)?1:0;}
   return 1-(a==1&&b==1?3:a+b+c)*.115f;
  }
 }
}

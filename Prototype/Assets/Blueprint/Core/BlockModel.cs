using System;
using System.Collections.Generic;

namespace SpatialWallpaper.Blueprint {
 public enum BlockRenderKind {Opaque,Cutout,Glass,Water,Glow}
 public struct Float3 {
  public float X,Y,Z;public Float3(float x,float y,float z){X=x;Y=y;Z=z;}
  public static Float3 operator +(Float3 a,Float3 b)=>new Float3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
  public static Float3 operator -(Float3 a,Float3 b)=>new Float3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
  public static Float3 operator *(Float3 a,float f)=>new Float3(a.X*f,a.Y*f,a.Z*f);
  public static Float3 Cross(Float3 a,Float3 b)=>new Float3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
  public Float3 Normalized(){float n=(float)Math.Sqrt(X*X+Y*Y+Z*Z);return n>0?this*(1/n):this;}
 }
 public struct Float2 {public float X,Y;public Float2(float x,float y){X=x;Y=y;}}
 public sealed class ModelQuad {
  public Float3[] Points;public Float2[] Uv;public Float3 Normal;public string Texture;
  public int CullFace=-1;public byte Tint;public BlockRenderKind Kind;public bool Wind;
 }
 public sealed class BlockModel {
  public List<ModelQuad> Quads {get;}=new List<ModelQuad>();
  public bool FullCube {get;internal set;}public bool Supported {get;internal set;}=true;
  public string Problem {get;internal set;}public bool Waterlogged {get;internal set;}
 }
 public struct TextureRegion {
  public float U0,V0,U1,V1;
  public Float2 Map(Float2 uv)=>new Float2(U0+(U1-U0)*uv.X,V0+(V1-V0)*uv.Y);
 }
 internal sealed class ShapeBox {
  public Float3 Min,Max;public string[] Textures;public byte[] Tints;public BlockRenderKind Kind;
  public ShapeBox(Float3 min,Float3 max,string texture,BlockRenderKind kind=BlockRenderKind.Opaque,byte tint=0){Min=min;Max=max;Textures=new[]{texture,texture,texture,texture,texture,texture};Tints=new[]{tint,tint,tint,tint,tint,tint};Kind=kind;}
 }
 internal struct FaceRect {
  public float A,B,C,D;public FaceRect(float a,float b,float c,float d){A=a;B=b;C=c;D=d;}
 }
 internal static class ShapeGeometry {
  const float E=.00001f;
  public static readonly Float3[] Directions={new Float3(1,0,0),new Float3(-1,0,0),new Float3(0,1,0),new Float3(0,-1,0),new Float3(0,0,1),new Float3(0,0,-1)};
  static float Plane(ShapeBox b,int f)=>f==0?b.Max.X:f==1?b.Min.X:f==2?b.Max.Y:f==3?b.Min.Y:f==4?b.Max.Z:b.Min.Z;
  static FaceRect Rect(ShapeBox b,int f)=>f<2?new FaceRect(b.Min.Z,b.Min.Y,b.Max.Z,b.Max.Y):f<4?new FaceRect(b.Min.X,b.Min.Z,b.Max.X,b.Max.Z):new FaceRect(b.Min.X,b.Min.Y,b.Max.X,b.Max.Y);
  static bool Covers(ShapeBox b,int f,float p)=>f==0?b.Min.X<=p+E&&b.Max.X>p+E:f==1?b.Max.X>=p-E&&b.Min.X<p-E:f==2?b.Min.Y<=p+E&&b.Max.Y>p+E:f==3?b.Max.Y>=p-E&&b.Min.Y<p-E:f==4?b.Min.Z<=p+E&&b.Max.Z>p+E:b.Max.Z>=p-E&&b.Min.Z<p-E;
  static List<FaceRect> Subtract(List<FaceRect> source,FaceRect cover){
   var result=new List<FaceRect>();foreach(var r in source){float a=Math.Max(r.A,cover.A),b=Math.Max(r.B,cover.B),c=Math.Min(r.C,cover.C),d=Math.Min(r.D,cover.D);
    if(c-a<=E||d-b<=E){result.Add(r);continue;}
    if(a-r.A>E)result.Add(new FaceRect(r.A,r.B,a,r.D));if(r.C-c>E)result.Add(new FaceRect(c,r.B,r.C,r.D));if(b-r.B>E)result.Add(new FaceRect(a,r.B,c,b));if(r.D-d>E)result.Add(new FaceRect(a,d,c,r.D));
   }return result;
  }
  public static void Boxes(BlockModel model,List<ShapeBox> boxes){
   foreach(var box in boxes)for(int face=0;face<6;face++){
    float plane=Plane(box,face);var parts=new List<FaceRect>{Rect(box,face)};
    foreach(var other in boxes)if(other!=box&&Covers(other,face,plane)){parts=Subtract(parts,Rect(other,face));if(parts.Count==0)break;}
    foreach(var r in parts)model.Quads.Add(Face(face,plane,r,box.Textures[face],box.Kind,box.Tints[face]));
   }
  }
  public static void Fluid(BlockModel model,List<ShapeBox> solids,string texture){
   var fluid=new ShapeBox(new Float3(0,0,0),new Float3(1,.888889f,1),texture,BlockRenderKind.Water);
   for(int face=0;face<6;face++){
    float plane=Plane(fluid,face);var parts=new List<FaceRect>{Rect(fluid,face)};
    foreach(var solid in solids){float low=face<2?solid.Min.X:face<4?solid.Min.Y:solid.Min.Z,high=face<2?solid.Max.X:face<4?solid.Max.Y:solid.Max.Z;
     if(plane>=low-E&&plane<=high+E)parts=Subtract(parts,Rect(solid,face));
    }
    foreach(var rect in parts)model.Quads.Add(Face(face,plane,rect,texture,BlockRenderKind.Water,0));
   }model.Waterlogged=true;
  }
  public static ModelQuad Face(int face,float p,FaceRect r,string texture,BlockRenderKind kind,byte tint){
   Float3[] points;
   switch(face){
    case 0:points=new[]{new Float3(p,r.B,r.A),new Float3(p,r.D,r.A),new Float3(p,r.D,r.C),new Float3(p,r.B,r.C)};break;
    case 1:points=new[]{new Float3(p,r.B,r.C),new Float3(p,r.D,r.C),new Float3(p,r.D,r.A),new Float3(p,r.B,r.A)};break;
    case 2:points=new[]{new Float3(r.A,p,r.D),new Float3(r.C,p,r.D),new Float3(r.C,p,r.B),new Float3(r.A,p,r.B)};break;
    case 3:points=new[]{new Float3(r.A,p,r.B),new Float3(r.C,p,r.B),new Float3(r.C,p,r.D),new Float3(r.A,p,r.D)};break;
    case 4:points=new[]{new Float3(r.C,r.B,p),new Float3(r.C,r.D,p),new Float3(r.A,r.D,p),new Float3(r.A,r.B,p)};break;
    default:points=new[]{new Float3(r.A,r.B,p),new Float3(r.A,r.D,p),new Float3(r.C,r.D,p),new Float3(r.C,r.B,p)};break;
   }
   var uv=new Float2[4];for(int i=0;i<4;i++){var v=points[i];uv[i]=face<2?new Float2(face==0?v.Z:1-v.Z,v.Y):face<4?new Float2(v.X,face==2?1-v.Z:v.Z):new Float2(face==4?1-v.X:v.X,v.Y);}
   return new ModelQuad{Points=points,Uv=uv,Normal=Directions[face],Texture=texture,Kind=kind,Tint=tint,Wind=tint==2&&kind==BlockRenderKind.Cutout,CullFace=Math.Abs(p-(face%2==0?1:0))<E?face:-1};
  }
  public static void Cross(BlockModel model,string texture,BlockRenderKind kind=BlockRenderKind.Cutout,byte tint=0,float height=1){
   Plane(model,new[]{new Float3(.1464f,0,.1464f),new Float3(.1464f,height,.1464f),new Float3(.8536f,height,.8536f),new Float3(.8536f,0,.8536f)},texture,kind,tint,true,texture!="chain");
   Plane(model,new[]{new Float3(.1464f,0,.8536f),new Float3(.1464f,height,.8536f),new Float3(.8536f,height,.1464f),new Float3(.8536f,0,.1464f)},texture,kind,tint,true,texture!="chain");
  }
  public static void Plane(BlockModel model,Float3[] points,string texture,BlockRenderKind kind,byte tint,bool bothSides,bool wind=false){
   var normal=Float3.Cross(points[1]-points[0],points[2]-points[0]).Normalized();var uv=new[]{new Float2(0,0),new Float2(0,1),new Float2(1,1),new Float2(1,0)};
   model.Quads.Add(new ModelQuad{Points=points,Uv=uv,Normal=normal,Texture=texture,Kind=kind,Tint=tint,Wind=wind});
   if(bothSides)model.Quads.Add(new ModelQuad{Points=new[]{points[3],points[2],points[1],points[0]},Uv=new[]{uv[3],uv[2],uv[1],uv[0]},Normal=normal*(-1),Texture=texture,Kind=kind,Tint=tint,Wind=wind});
  }
  public static void RotateY(BlockModel model,float degrees,Float3 pivot){
   if(degrees==0)return;float angle=degrees*(float)Math.PI/180,c=(float)Math.Cos(angle),s=(float)Math.Sin(angle);
   Func<Float3,Float3> rotate=v=>new Float3(c*v.X+s*v.Z,v.Y,-s*v.X+c*v.Z);
   foreach(var quad in model.Quads){for(int i=0;i<4;i++)quad.Points[i]=rotate(quad.Points[i]-pivot)+pivot;quad.Normal=rotate(quad.Normal);quad.CullFace=-1;
    for(int f=0;f<6;f++){var n=Directions[f];if(Math.Abs(n.X-quad.Normal.X)+Math.Abs(n.Y-quad.Normal.Y)+Math.Abs(n.Z-quad.Normal.Z)>.001f)continue;bool boundary=true;foreach(var p in quad.Points){float v=f<2?p.X:f<4?p.Y:p.Z;if(Math.Abs(v-(f%2==0?1:0))>.001f)boundary=false;}if(boundary)quad.CullFace=f;}
   }
  }
 }
}

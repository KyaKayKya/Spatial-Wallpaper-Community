using System;
using System.Collections.Generic;
using System.Linq;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintPlacement {
  public float Scale,Yaw,X,Y,Z;public float GroundY=>-.298125f/2;
  public int MinX,MinY,MinZ,Width,Height,Length;
  public float CenterX=>MinX+Width*.5f;public float CenterZ=>MinZ+Length*.5f;
 }
 public static class BlueprintLayout {
  public static BlueprintPlacement Fit(BlueprintDocument document,int version=2){
   int minX=document.Width,minY=document.Height,minZ=document.Length,maxX=-1,maxY=-1,maxZ=-1;
   var counts=new Dictionary<string,int>(StringComparer.Ordinal);for(int i=0;i<document.Indices.Length;i++){int id=document.Indices[i];if(id==0)continue;int x=i%document.Width,z=i/document.Width%document.Length,y=i/(document.Width*document.Length);minX=Math.Min(minX,x);minY=Math.Min(minY,y);minZ=Math.Min(minZ,z);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);maxZ=Math.Max(maxZ,z);var state=document.Palette[id];if(!state.Name.EndsWith("_door",StringComparison.Ordinal)||state.Property("half","lower")!="lower")continue;string facing=state.Property("facing","north");counts[facing]=counts.TryGetValue(facing,out int n)?n+1:1;}
   if(maxX<0)throw new BlueprintImportException("empty-blueprint");
   if(version==1){minX=minY=minZ=0;maxX=document.Width-1;maxY=document.Height-1;maxZ=document.Length-1;}
   int width=maxX-minX+1,height=maxY-minY+1,length=maxZ-minZ+1;
   string front=counts.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key).FirstOrDefault()??"north";
   float scale=Math.Min(.04f,Math.Min(.4f/height,.8f/Math.Max(width,length)));
   return new BlueprintPlacement{Scale=scale,Yaw=front=="east"?90:front=="south"?180:front=="west"?270:0,X=0,Y=-.298125f/2,Z=.22f+(float)Math.Sqrt(width*width+length*length)*scale/2,MinX=minX,MinY=minY,MinZ=minZ,Width=width,Height=height,Length=length};
  }
 }
}

using System;
namespace SpatialWallpaper.Blueprint {
 public struct BlueprintFootprint {
  public float MinX,MaxX,MinZ,MaxZ,BaseY,Scale;
  public bool Contains(float x,float z,float margin=0)=>x>=MinX-margin&&x<=MaxX+margin&&z>=MinZ-margin&&z<=MaxZ+margin;
  public static BlueprintFootprint For(BlueprintDocument d,BlueprintPlacement layout,BlueprintSettings settings){float scale=layout.Scale*settings.Scale,angle=(layout.Yaw+settings.Yaw)*(float)Math.PI/180;float c=Math.Abs((float)Math.Cos(angle)),s=Math.Abs((float)Math.Sin(angle)),rx=(layout.Width*c+layout.Length*s)*scale*.5f,rz=(layout.Width*s+layout.Length*c)*scale*.5f,x=layout.X+settings.OffsetX,z=layout.Z+settings.OffsetZ;return new BlueprintFootprint{MinX=x-rx,MaxX=x+rx,MinZ=z-rz,MaxZ=z+rz,BaseY=layout.Y+settings.OffsetY,Scale=scale};}
 }
}

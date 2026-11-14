using System;
using System.IO;
namespace SpatialWallpaper.Sdk {
 public static class Framing {
  public static double Scale(double authorWidth,double authorHeight,double viewportWidth,double viewportHeight,bool keepHeight){
   foreach(double value in new[]{authorWidth,authorHeight,viewportWidth,viewportHeight})
    if(double.IsNaN(value)||double.IsInfinity(value)||value<=0)throw new InvalidDataException("invalid-frame");
   return keepHeight?viewportHeight/authorHeight:viewportWidth/authorWidth;
  }
 }
}

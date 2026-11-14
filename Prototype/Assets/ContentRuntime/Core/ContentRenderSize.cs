using System;
using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Content {
 public static class ContentRenderSize {
  // Preserve native detail inside the existing validated render envelope.
  public static void Choose(int targetWidth,int targetHeight,out int width,out int height){
   if(targetWidth<1||targetHeight<1||targetWidth>8192||targetHeight>8192)throw new ArgumentOutOfRangeException("content-target-size");
   if(targetWidth<=RuntimeLimits.MaxRenderSide&&targetHeight<=RuntimeLimits.MaxRenderSide&&(long)targetWidth*targetHeight<=RuntimeLimits.MaxRenderPixels){width=targetWidth;height=targetHeight;return;}
   double aspect=(double)targetWidth/targetHeight;int best=0;width=height=0;
   for(int h=1;h<=Math.Min(RuntimeLimits.MaxRenderSide,targetHeight);h++){int w=(int)Math.Round(h*aspect);if(w<1||w>RuntimeLimits.MaxRenderSide||w>targetWidth||(long)w*h>RuntimeLimits.MaxRenderPixels||Math.Abs((double)w/h-aspect)>.00005)continue;if(w*h>best){width=w;height=h;best=w*h;}}
   if(best==0)throw new NotSupportedException("content-target-aspect-unavailable");
  }
 }
}

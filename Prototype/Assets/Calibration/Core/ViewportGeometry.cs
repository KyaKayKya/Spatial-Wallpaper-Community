namespace SpatialTracking {
 public sealed class ViewportMetrics {
  public readonly double Width,Height,CenterX,CenterY;
  public ViewportMetrics(double width,double height,double x,double y){Width=width;Height=height;CenterX=x;CenterY=y;}
  public double[] ToView(double[] displayEye){return new[]{displayEye[0]-CenterX,displayEye[1]-CenterY,displayEye[2]};}
 }
 // Desktop coordinates may be negative on a secondary monitor. Minimized windows
 // may instead report a far-off sentinel rectangle; that is not a physical viewport.
 public static class ViewportGeometry {
  // Monitor/client in physical desktop pixels; viewport in render-buffer bottom-left pixels.
  public static bool TryMap(int mx,int my,int mw,int mh,int cx,int cy,int cw,int ch,int rw,int rh,
   double vx,double vy,double vw,double vh,double displayWidth,out ViewportMetrics result){
   result=null;
   foreach(var v in new[]{vx,vy,vw,vh,displayWidth})if(!TrackingCalibration.Finite(v))return false;
   if(mw<=0||mh<=0||cw<=0||ch<=0||rw<=0||rh<=0||vw<=0||vh<=0||displayWidth<=0||vx<-.01||vy<-.01||vx+vw>rw+.01||vy+vh>rh+.01)return false;
   double sx=(double)cw/rw,sy=(double)ch/rh;
   double left=cx+vx*sx,top=cy+(rh-vy-vh)*sy,right=left+vw*sx,bottom=top+vh*sy;
   if(left<mx-.5||right>mx+(double)mw+.5||top<my-.5||bottom>my+(double)mh+.5)return false;
   double unit=displayWidth/mw;
   result=new ViewportMetrics(vw*sx*unit,vh*sy*unit,((left+right)/2-mx-mw/2.0)*unit,(my+mh/2.0-(top+bottom)/2)*unit);
   return true;
  }
  public static bool ClientIntersectsMonitor(int x,int y,int width,int height,int left,int top,int right,int bottom){
   return width>0&&height>0&&right>left&&bottom>top&&
    (long)x+width>left&&x<right&&(long)y+height>top&&y<bottom;
  }
 }
}

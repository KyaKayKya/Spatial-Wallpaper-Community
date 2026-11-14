using System;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
// Explicit visual acceptance only. Product presentation never calls this code.
static class ScreenFrameValidation {
 [DllImport("gdi32.dll")]static extern bool GdiFlush();
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
 public static object Present(Type kind,object presenter,object[] args){
  var receipt=kind.GetMethod("Present").Invoke(presenter,new[]{args[0],args[1]});
  if(args[2] is string path){var target=(Control)kind.GetField("target",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(presenter);Verify(target,(RuntimeFrame)args[1],path);}
  return receipt;
 }
 static void Verify(Control target,RuntimeFrame frame,string path){
  if(!GdiFlush())throw new Exception("validation-GDI-flush");int error=DwmFlush();if(error<0)Marshal.ThrowExceptionForHR(error);
  var expected=new byte[frame.Width*frame.Height*4];frame.CopyPixels(expected);
  using(var capture=new Bitmap(frame.Width,frame.Height))using(var wanted=new Bitmap(frame.Width,frame.Height)){
   using(var graphics=Graphics.FromImage(capture))graphics.CopyFromScreen(target.PointToScreen(Point.Empty),Point.Empty,capture.Size);
   int differences=0;for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++){int at=(y*frame.Width+x)*4;var color=Color.FromArgb(expected[at],expected[at+1],expected[at+2]);wanted.SetPixel(x,y,color);var actual=capture.GetPixel(x,y);if(actual.R!=color.R||actual.G!=color.G||actual.B!=color.B)differences++;}
   capture.Save(path);
   if(differences!=0){wanted.Save(Path.ChangeExtension(path,"expected.png"));File.WriteAllText(Path.ChangeExtension(path,"mismatch.json"),new JObject{{"different_pixels",differences},{"sequence",frame.Sequence},{"projection_revision",frame.ProjectionRevision},{"scope","test-only-full-screen-RGB"}}.ToString());throw new InvalidOperationException("presenter-screen-readback-mismatch");}
  }
 }
}

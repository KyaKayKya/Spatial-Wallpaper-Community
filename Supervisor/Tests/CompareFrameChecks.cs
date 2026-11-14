using System;using System.IO;using System.Drawing;using System.Drawing.Imaging;using System.Runtime.InteropServices;using Newtonsoft.Json.Linq;
static class CompareFrameChecks {
 static int Main(string[] args){try{
  using(var expected=new Bitmap(args[0])){
   int width=expected.Width,height=expected.Height;long count=(long)width*height*4;
   if(width<1||height<1||width>8192||height>8192||count>64*1024*1024)throw new Exception("trusted-baseline-dimensions");
   byte[] actual=File.ReadAllBytes(args[1]);if(actual.Length!=count)throw new Exception("frame-file-size");
   long sum=0;int max=0;bool same=true,vertical=true,horizontal=true;
   using(var result=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
    var data=result.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);var row=new byte[width*4];
    try{for(int y=0;y<height;y++){
     for(int x=0;x<width;x++){
      int offset=(y*width+x)*4;Color e=expected.GetPixel(x,y),v=expected.GetPixel(x,height-1-y),h=expected.GetPixel(width-1-x,y);
      int r=actual[offset],g=actual[offset+1],b=actual[offset+2];int dr=Math.Abs(r-e.R),dg=Math.Abs(g-e.G),db=Math.Abs(b-e.B);sum+=dr+dg+db;max=Math.Max(max,Math.Max(dr,Math.Max(dg,db)));
      same&=dr==0&&dg==0&&db==0;vertical&=r==v.R&&g==v.G&&b==v.B;horizontal&=r==h.R&&g==h.G&&b==h.B;
      row[x*4]=(byte)b;row[x*4+1]=(byte)g;row[x*4+2]=(byte)r;row[x*4+3]=255;
     }
     Marshal.Copy(row,0,IntPtr.Add(data.Scan0,y*data.Stride),row.Length);
    }}finally{result.UnlockBits(data);}
    result.Save(args[2],ImageFormat.Png);
   }
   var report=new JObject{{"rgb_exact",same},{"vertical_flip_equal",vertical},{"horizontal_flip_equal",horizontal},{"maximum_channel_error",max},{"mean_absolute_channel_error",(double)sum/(width*height*3)}};
   File.WriteAllText(args[3],report.ToString());Console.WriteLine(report);
   if(!same||vertical||horizontal)throw new Exception("asymmetric-frame-fidelity-or-orientation");
  }
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}

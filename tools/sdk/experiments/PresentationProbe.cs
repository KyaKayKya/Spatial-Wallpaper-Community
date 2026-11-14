// Trusted surface only. Reads a fixed-size RGB buffer; never loads scene content.
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SpatialWallpaper.Desktop;
class PresentationProbe {
 [STAThread] static int Main(string[] args) {
  string mode=args[0],output=args[2];var data=File.ReadAllBytes(args[1]);
  if(data.Length!=640*360*3)throw new InvalidDataException("frame-size");
  using(var bitmap=new Bitmap(640,360,PixelFormat.Format24bppRgb)){
   var locked=bitmap.LockBits(new Rectangle(0,0,640,360),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
   try{for(int y=0;y<360;y++){var row=new byte[640*3];for(int x=0;x<640;x++){int source=(y*640+x)*3;row[x*3]=data[source+2];row[x*3+1]=data[source+1];row[x*3+2]=data[source];}Marshal.Copy(row,0,IntPtr.Add(locked.Scan0,y*locked.Stride),row.Length);}}
   finally{bitmap.UnlockBits(locked);}
   using(var form=new Form{Text="SDK Stage A trusted frame surface",ClientSize=new Size(640,360),ShowInTaskbar=false})
   using(var image=new PictureBox{Dock=DockStyle.Fill,Image=bitmap,SizeMode=PictureBoxSizeMode.StretchImage})
   using(var timer=new Timer{Interval=500})
   using(var desktop=new WindowsDesktopWindow()){
    form.Controls.Add(image);string error=null;bool verified=false;
    form.Shown+=(sender,evt)=>{
     try{
      if(mode=="desktop"){var displays=WindowsDesktopWindow.Displays();if(displays.Length==0)throw new Exception("no-display");desktop.Attach(form.Handle,displays[0].Id);verified=desktop.Verify();}
      else if(mode=="preview"){
       using(var capture=new Bitmap(640,360)){image.DrawToBitmap(capture,new Rectangle(0,0,640,360));capture.Save(Path.Combine(Path.GetDirectoryName(output),"preview.png"),ImageFormat.Png);verified=capture.GetPixel(320,180).ToArgb()==bitmap.GetPixel(320,180).ToArgb();}
      } else throw new Exception("unsupported-mode");
     }catch(Exception e){error=e.GetType().Name+":"+e.Message;}
     timer.Start();
    };
    timer.Tick+=(sender,evt)=>{timer.Stop();desktop.Detach();form.Close();};
    Application.Run(form);
    File.WriteAllText(output,"{\"mode\":\""+mode+"\",\"verified\":"+verified.ToString().ToLowerInvariant()+",\"error\":\""+(error??"").Replace("\\","/").Replace("\"","'")+"\"}");
    return verified&&error==null?0:1;
   }
  }
 }
}

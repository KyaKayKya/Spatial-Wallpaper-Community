using System;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Interactive;
static class ContentDesktopInputChecks {
 struct Point{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct Payload{[FieldOffset(0)]public Mouse Mouse;}
 // INPUT's union also contains KEYBDINPUT/HARDWAREINPUT; its x64 size is 40.
 [StructLayout(LayoutKind.Sequential)]struct Event{public uint Type;public Payload Data;}
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Event[] events,int size);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 sealed class Cover:Form{protected override bool ShowWithoutActivation=>true;}
 static void Need(bool yes,string reason){if(!yes)throw new Exception(reason);}
 static void Pump(int ms){var time=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(time.ElapsedMilliseconds<ms);}
 // This fixture's complete triangle stays in the center third. Desktop icons
 // outside that authored region must not affect its measured centroid.
 static double Center(Rectangle bounds,string path){using(var image=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb)){using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(bounds.Location,System.Drawing.Point.Empty,bounds.Size);image.Save(path);var data=image.LockBits(new Rectangle(System.Drawing.Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{var row=new byte[bounds.Width*4];long sum=0,count=0;for(int y=bounds.Height/8;y<bounds.Height*7/8;y++){Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);for(int x=bounds.Width/3;x<bounds.Width*2/3;x++)if(row[x*4+2]>150&&row[x*4+1]<100&&row[x*4]<100){sum+=x;count++;}}return count>1000?(double)sum/count:double.NaN;}finally{image.UnlockBits(data);}}}
 public static JObject Run(string output,string runtime,JObject status){
  IntPtr previous=GetForegroundWindow();GetCursorPos(out var cursor);object shell=null;bool minimized=false,injected=false;
  var bounds=Screen.PrimaryScreen.Bounds;Need((int)status["receipt"]["width"]==bounds.Width&&(int)status["receipt"]["height"]==bounds.Height,"desktop-input-primary-display-fixture-size");
  try{foreach(int key in new[]{1,2,16,17,18,32,91,92})Need((GetAsyncKeyState(key)&0x8000)==0,"desktop-input-preexisting-held-key");
   shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));shell.GetType().InvokeMember("MinimizeAll",System.Reflection.BindingFlags.InvokeMethod,null,shell,null);minimized=true;Pump(250);
   using(var probe=new DesktopHitClient(Path.Combine(runtime,"DesktopHitTester.exe"))){System.Drawing.Point blank=System.Drawing.Point.Empty;bool found=false;for(int y=bounds.Top+120;y<bounds.Bottom-100&&!found;y+=80)for(int x=bounds.Left+200;x<bounds.Right/2;x+=80){var hit=probe.Query(x,y);if(hit!=null&&hit.Classification==1){blank=new System.Drawing.Point(x,y);found=true;break;}}Need(found,"desktop-input-blank-desktop-not-found");
    Need(SetCursorPos(blank.X,blank.Y),"desktop-input-position-pointer");Pump(150);double baseline=Center(bounds,Path.Combine(output,"desktop-before-input.png"));Need(!double.IsNaN(baseline),"desktop-input-visible-author-pixels-missing");
    GetWindowThreadProcessId(GetShellWindow(),out var shellPid);GetWindowThreadProcessId(GetForegroundWindow(),out var foregroundPid);if(foregroundPid!=shellPid){SetForegroundWindow(GetShellWindow());Pump(100);GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);}Need(foregroundPid==shellPid,"desktop-input-shell-not-foreground-before-click");
    var fresh=probe.Query(blank.X,blank.Y);Need(fresh!=null&&fresh.Classification==1&&fresh.IsFresh(SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds()),"desktop-input-click-no-longer-blank");
    injected=true;Need(SendInput(2,new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Event)))==2,"desktop-input-real-click-send");
    var clock=Stopwatch.StartNew();double feedback=baseline;while(clock.ElapsedMilliseconds<5000){Pump(100);GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);if(foregroundPid==(uint)(int)status["viewer_pid"]){feedback=Center(bounds,Path.Combine(output,"desktop-focused-input.png"));if(feedback>baseline+10)break;}}
    Need(foregroundPid==(uint)(int)status["viewer_pid"]&&feedback>baseline+10,"desktop-presented-real-click-did-not-reach-author");
    using(var cover=new Cover{TopMost=true,FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(bounds.Left+60,bounds.Top+60,120,80),BackColor=Color.Blue}){cover.Show();Need(SetCursorPos(cover.Left+30,cover.Top+30),"desktop-input-pointer-to-other-window");clock.Restart();double cleared=feedback;while(clock.ElapsedMilliseconds<5000){Pump(100);cleared=Center(bounds,Path.Combine(output,"desktop-excluded-input.png"));if(Math.Abs(cleared-baseline)<3)break;}Need(Math.Abs(cleared-baseline)<3,"desktop-other-window-retained-author-input");return new JObject{{"native_desktop_click",true},{"baseline_center",baseline},{"focused_center",feedback},{"excluded_center",cleared},{"blank_x",blank.X},{"blank_y",blank.Y},{"scope","actual Explorer blank click, original desktop child, ordinary author pointer API and visible feedback; own excluded window revokes; not full input/RC matrix"}};}
   }
  }finally{if(injected)SendInput(1,new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Event)));if(minimized)try{shell.GetType().InvokeMember("UndoMinimizeALL",System.Reflection.BindingFlags.InvokeMethod,null,shell,null);}finally{Marshal.FinalReleaseComObject(shell);}SetCursorPos(cursor.X,cursor.Y);if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}
 }
}

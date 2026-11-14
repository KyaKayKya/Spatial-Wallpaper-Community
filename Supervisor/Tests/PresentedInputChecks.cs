using System;using System.Drawing;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
static class PresentedInputChecks {
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct Payload {[FieldOffset(0)]public Mouse Mouse;[FieldOffset(0)]public Keyboard Key;}
 [StructLayout(LayoutKind.Sequential)]struct Event {public uint Type;public Payload Data;}
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Event[] values,int size);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Pump(int ms){var timer=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(timer.ElapsedMilliseconds<ms);}
 static double Center(RuntimeFrame frame){var p=new byte[frame.Width*frame.Height*4];frame.CopyPixels(p);long sum=0,count=0;for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++){int at=(y*frame.Width+x)*4;if(p[at]>150&&p[at+1]<100&&p[at+2]<100){sum+=x;count++;}}Need(count>100,"input-feedback-author-geometry-missing");return (double)sum/count;}
 static void Click(Form form){Need(GetForegroundWindow()==form.Handle,"input-feedback-owned-foreground");Need(SendInput(2,new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Event)))==2,"input-feedback-real-click-delivery");Pump(70);}
 static double ScreenCenter(Control surface){using(var image=new Bitmap(surface.ClientSize.Width,surface.ClientSize.Height)){using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(surface.PointToScreen(System.Drawing.Point.Empty),System.Drawing.Point.Empty,image.Size);long sum=0,count=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var pixel=image.GetPixel(x,y);if(pixel.R>150&&pixel.G<100&&pixel.B<100){sum+=x;count++;}}Need(count>100,"preview-input-author-red-pixels-missing");return (double)sum/count;}}
 public static bool ManualFocusWait;
 public static async System.Threading.Tasks.Task<JObject> WindowFeedback(Form form,Control surface,Func<JObject> receipt){
  IntPtr previous=GetForegroundWindow();GetCursorPos(out var cursor);bool injected=false;try{foreach(int key in new[]{1,2,16,17,18,32,91,92})Need((GetAsyncKeyState(key)&0x8000)==0,"input-feedback-preexisting-held-key");form.Activate();bool activated=SetForegroundWindow(form.Handle);if(ManualFocusWait){Console.WriteLine("WAITING user activation of "+form.Text);var focusDeadline=Stopwatch.StartNew();while(GetForegroundWindow()!=form.Handle&&focusDeadline.ElapsedMilliseconds<60000)await System.Threading.Tasks.Task.Delay(25);}IntPtr observedForeground=GetForegroundWindow();Need(observedForeground==form.Handle,"preview-input-owned-foreground: expected="+form.Handle.ToInt64()+" observed="+observedForeground.ToInt64()+" setForeground="+activated);var point=surface.PointToScreen(new System.Drawing.Point(160,270));Need(SetCursorPos(point.X,point.Y),"preview-input-position-pointer");await System.Threading.Tasks.Task.Delay(100);long before=(long)receipt()["sequence"];injected=true;Need(SendInput(2,new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Event)))==2,"preview-input-real-click-delivery");double focused=0;var timer=Stopwatch.StartNew();do{await System.Threading.Tasks.Task.Delay(50);if((long)receipt()["sequence"]>before)focused=ScreenCenter(surface);}while((focused<340||focused>345)&&timer.ElapsedMilliseconds<5000);Need(focused>340&&focused<345,"preview-window-input-did-not-reach-author");point=surface.PointToScreen(new System.Drawing.Point(-8,270));Need(SetCursorPos(point.X,point.Y),"preview-input-position-outside");double cleared=0;timer.Restart();do{await System.Threading.Tasks.Task.Delay(50);cleared=ScreenCenter(surface);}while(Math.Abs(cleared-319.5)>2&&timer.ElapsedMilliseconds<5000);Need(Math.Abs(cleared-319.5)<2,"preview-window-outside-region-retained-input");return new JObject{{"native_click",true},{"visible_author_feedback",focused},{"outside_region",cleared}};
  }finally{if(injected)SendInput(1,new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Event)));SetCursorPos(cursor.X,cursor.Y);if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}
 }
 public static JObject Run(Form form,object presenter,RuntimeSession session,RuntimeSession foreignSession,SupervisorManagerConnection connection,Func<RuntimeFrame> next,Action<RuntimeFrame> present,bool noAdapter){
  IntPtr previous=GetForegroundWindow();GetCursorPos(out var cursor);IDisposable adapter=null;
  try{
   foreach(int key in new[]{1,2,16,17,18,32,91,92})Need((GetAsyncKeyState(key)&0x8000)==0,"input-feedback-preexisting-held-key");
   var adapterType=typeof(PresentedInputChecks).Assembly.GetType("SpatialWallpaper.Supervision.Presentation.WindowsPresentedPreviewInput");Need(adapterType!=null,"presented-preview-input-adapter-unavailable");
   bool foreignRejected=false;try{using(var wrong=(IDisposable)Activator.CreateInstance(adapterType,new object[]{form,presenter,foreignSession,connection})){} }catch(System.Reflection.TargetInvocationException error){foreignRejected=error.InnerException.Message=="preview-input-presentation-session";}Need(foreignRejected,"foreign-session-presented-input-accepted");
   using(var other=new Panel()){bool targetRejected=false;try{using(var wrong=(IDisposable)Activator.CreateInstance(adapterType,new object[]{other,presenter,session,connection})){} }catch(System.Reflection.TargetInvocationException error){targetRejected=error.InnerException.Message=="preview-input-presentation-target";}Need(targetRejected,"foreign-target-presented-input-accepted");}
   if(!noAdapter)adapter=(IDisposable)Activator.CreateInstance(adapterType,new object[]{form,presenter,session,connection});
   if(adapter!=null){string rejection=System.Threading.Tasks.Task.Run(()=>{try{adapter.Dispose();return "accepted";}catch(InvalidOperationException error){return error.Message;}}).GetAwaiter().GetResult();Need(rejection=="preview-input-owner-thread","wrong-thread-input-dispose-not-rejected");}
   form.Activate();SetForegroundWindow(form.Handle);var point=form.PointToScreen(new System.Drawing.Point(160,270));Need(SetCursorPos(point.X,point.Y),"input-feedback-position-pointer");Pump(70);Click(form);
   var frame=next();Need(Math.Abs(Center(frame)-319.5)<2,"input-before-first-presentation-reached-author");present(frame);Click(form);
   double focused=0;var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<5000){Pump(10);frame=next();focused=Center(frame);if(focused>340&&focused<345)break;}Need(focused>340&&focused<345,"presented-real-input-did-not-reach-author");present(frame);
   // Outside the exact rendered client rectangle must clear ordinary author input.
   point=form.PointToScreen(new System.Drawing.Point(-20,270));SetCursorPos(point.X,point.Y);Pump(100);double cleared=Center(next());Need(Math.Abs(cleared-319.5)<2,"outside-presented-region-retained-author-input");
   return new JObject{{"native_click",true},{"before_present_rejected",true},{"author_feedback_center",focused},{"outside_region_center",cleared},{"same_presented_region",true}};
  }finally{adapter?.Dispose();SetCursorPos(cursor.X,cursor.Y);if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}
 }
}

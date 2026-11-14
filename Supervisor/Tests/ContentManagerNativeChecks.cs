using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

// Test-only Windows shell drop delivery and actual mouse activation of the
// original Unity button. No reflection or direct invocation of UI callbacks.
static class ContentManagerNativeChecks {
 delegate bool Visitor(IntPtr window,IntPtr context);
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct Input {public uint Type;public Mouse Mouse;}
 [DllImport("user32.dll")]static extern bool EnumWindows(Visitor visit,IntPtr context);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int count);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalLock(IntPtr memory);
 [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr memory);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr memory);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] inputs,int size);
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Until(Func<bool> condition,string reason,int timeout=30000){var clock=Stopwatch.StartNew();while(!condition()){Need(clock.ElapsedMilliseconds<timeout,reason);Thread.Sleep(25);}}
 static JObject[] Events(string file){if(!File.Exists(file))return new JObject[0];string text;using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))text=reader.ReadToEnd();return text.Split('\n').Where(line=>line.TrimEnd().EndsWith("}",StringComparison.Ordinal)).Select(JObject.Parse).ToArray();}
 static IntPtr Window(int pid){IntPtr result=IntPtr.Zero;EnumWindows((window,context)=>{GetWindowThreadProcessId(window,out var owner);var name=new StringBuilder(128);if(owner==pid&&GetClassName(window,name,128)>0&&name.ToString()=="UnityWndClass"){result=window;return false;}return true;},IntPtr.Zero);Need(result!=IntPtr.Zero,"native-manager-window-missing");return result;}
 static void Drop(IntPtr window,string path){
  byte[] names=Encoding.Unicode.GetBytes(Path.GetFullPath(path)+"\0\0"),bytes=new byte[20+names.Length];BitConverter.GetBytes(20).CopyTo(bytes,0);BitConverter.GetBytes(1).CopyTo(bytes,16);names.CopyTo(bytes,20);
  IntPtr memory=GlobalAlloc(0x42,new UIntPtr((uint)bytes.Length));Need(memory!=IntPtr.Zero,"native-manager-drop-allocation-failed");bool transferred=false;
  try{IntPtr address=GlobalLock(memory);Need(address!=IntPtr.Zero,"native-manager-drop-lock-failed");try{Marshal.Copy(bytes,0,address,bytes.Length);}finally{GlobalUnlock(memory);}Need(PostMessage(window,0x233,memory,IntPtr.Zero),"native-manager-drop-post-failed");transferred=true;}finally{if(!transferred)GlobalFree(memory);}
 }
 static void Capture(IntPtr window,string output){var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));try{Need(GetWindowRect(window,out var rectangle),"native-manager-capture-rectangle-missing");using(var bitmap=new System.Drawing.Bitmap(rectangle.Right-rectangle.Left,rectangle.Bottom-rectangle.Top)){using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rectangle.Left,rectangle.Top,0,0,bitmap.Size);bitmap.Save(output);}}finally{if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}}
 static void ClickOwned(IntPtr window,int x,int y,Func<bool> completed,string reason){Need(GetClientRect(window,out var rect),"native-manager-client-rectangle-missing");double scale=Math.Sqrt((rect.Right-rect.Left)/1120.0*(rect.Bottom-rect.Top)/760.0);var point=new Point{X=(int)Math.Round(x*scale),Y=(int)Math.Round(y*scale)};Need(ClientToScreen(window,ref point),"native-manager-click-geometry-unavailable");Need(GetCursorPos(out var original),"native-manager-cursor-unavailable");bool down=false;try{Need(SetCursorPos(point.X,point.Y)&&GetAncestor(GetForegroundWindow(),2)==window&&GetAncestor(WindowFromPoint(point),2)==window,"native-manager-audio-click-not-owned");Need(SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=2}}},Marshal.SizeOf(typeof(Input)))==1,"native-manager-audio-down-failed");down=true;Thread.Sleep(75);Need(SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=4}}},Marshal.SizeOf(typeof(Input)))==1,"native-manager-audio-up-failed");down=false;Until(completed,reason);}finally{try{if(down)SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=4}}},Marshal.SizeOf(typeof(Input)));}finally{SetCursorPos(original.X,original.Y);}}}
 public static JObject Run(Process manager,string output,string events,string package,bool manualFocus,bool audioPicker=false){
  IntPtr window=Window(manager.Id);
  if(audioPicker){ShowWindow(window,9);SetForegroundWindow(window);Thread.Sleep(150);if(GetAncestor(GetForegroundWindow(),2)!=window){File.WriteAllText(Path.Combine(output,"manual-focus-required"),"Click the SpatialWallpaper Content Manager title bar; no input has been sent.");Need(manualFocus,"native-manager-focus-required-before-audio-click");Until(()=>GetAncestor(GetForegroundWindow(),2)==window,"native-manager-manual-focus-timeout",60000);}ClickOwned(window,950,194,()=>Events(events).Any(item=>(string)item["event"]=="audio-picker-opened"),"native-manager-audio-picker-did-not-open");Thread.Sleep(200);Capture(window,Path.Combine(output,"manager-audio-picker.png"));ClickOwned(window,550,230,()=>Events(events).Any(item=>(string)item["event"]=="audio-source-selected"),"native-manager-no-audio-choice-did-not-complete");Need(Events(events).Last(item=>(string)item["event"]=="audio-source-selected")["data"]["source"].Type==JTokenType.Null,"native-manager-no-audio-choice-selected-source");}
  string bad=Path.Combine(output,"invalid-native-drop.spatial");File.WriteAllText(bad,"invalid package for negative UI import");Drop(window,bad);
  Until(()=>Events(events).Any(item=>(string)item["event"]=="import-failed"),"native-manager-invalid-drop-not-rejected");Need(!Events(events).Any(item=>(string)item["event"]=="import-completed"),"native-manager-invalid-drop-imported");
  Drop(window,package);Until(()=>Events(events).Any(item=>(string)item["event"]=="library-rendered"&&(int?)item["data"]?["works"]==1),"native-manager-valid-drop-not-rendered");
  var imported=Events(events).Last(item=>(string)item["event"]=="import-completed");ShowWindow(window,9);SetForegroundWindow(window);Thread.Sleep(150);
  if(GetAncestor(GetForegroundWindow(),2)!=window){File.WriteAllText(Path.Combine(output,"manual-focus-required"),"Click the SpatialWallpaper Content Manager title bar; no input has been sent.");Need(manualFocus,"native-manager-focus-required-before-click");Until(()=>GetAncestor(GetForegroundWindow(),2)==window,"native-manager-manual-focus-timeout",60000);}
  Need(GetClientRect(window,out var rect),"native-manager-client-rectangle-missing");double scale=Math.Sqrt((rect.Right-rect.Left)/1120.0*(rect.Bottom-rect.Top)/760.0);var point=new Point{X=(int)Math.Round(998*scale),Y=(int)Math.Round(370*scale)};Need(ClientToScreen(window,ref point),"native-manager-client-origin-missing");
  File.WriteAllText(Path.Combine(output,"native-manager-click-geometry.json"),new JObject{{"client_width",rect.Right-rect.Left},{"client_height",rect.Bottom-rect.Top},{"scale",scale},{"dpi",GetDpiForWindow(window)},{"screen_point",new JArray(point.X,point.Y)}}.ToString());Capture(window,Path.Combine(output,"manager-before-apply.png"));
  Need(GetCursorPos(out var original),"native-manager-cursor-unavailable");bool down=false;try{
   Need(SetCursorPos(point.X,point.Y),"native-manager-cursor-move-failed");Need(GetAncestor(GetForegroundWindow(),2)==window&&GetAncestor(WindowFromPoint(point),2)==window,"native-manager-click-target-no-longer-owned");
   Need(SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=2}}},Marshal.SizeOf(typeof(Input)))==1,"native-manager-mouse-down-rejected");down=true;Thread.Sleep(75);
   Need(SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=4}}},Marshal.SizeOf(typeof(Input)))==1,"native-manager-mouse-up-rejected");down=false;
   // Unity samples pointer-up on its next frame. Restoring the cursor before
   // that frame moves the release outside the button and cancels the click.
   Until(()=>Events(events).Any(item=>(string)item["event"]=="apply-result"),"native-manager-apply-button-produced-no-result");
  }finally{try{if(down)Need(SendInput(1,new[]{new Input{Mouse=new Mouse{Flags=4}}},Marshal.SizeOf(typeof(Input)))==1,"native-manager-mouse-up-rejected");}finally{SetCursorPos(original.X,original.Y);}}
  var applied=Events(events).Last(item=>(string)item["event"]=="apply-result");Need((bool)applied["data"]["succeeded"]&&(string)applied["data"]["work_id"]==(string)imported["data"]["work_id"],"native-manager-apply-button-failed");
  Until(()=>Events(events).Any(item=>(string)item["event"]=="library-rendered"&&(bool?)item["data"]?["presentation_ready"]==true),"native-manager-apply-did-not-present");
  return new JObject{{"audio_picker_opened_and_no_capture_selected",audioPicker},{"invalid_native_drop_rejected",true},{"valid_native_drop_imported",imported["data"].DeepClone()},{"actual_mouse_apply",applied["data"].DeepClone()},{"click_screen",new JArray(point.X,point.Y)},{"scope","real WM_DROPFILES through installed PackageDropReceiver, actual foreground/owned-target checked SendInput click; not an Explorer drag gesture"}};
 }
}

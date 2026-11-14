using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using SpatialWallpaper.Sdk.Hosting;
using UnityEngine;

public sealed partial class MainManagerSmoke {
 [StructLayout(LayoutKind.Sequential)]struct PreviewBounds{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out PreviewBounds bounds);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern bool AttachThreadInput(uint first,uint second,bool attach);
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 readonly HashSet<int> previewInjected=new HashSet<int>();
 void PreviewLedger(uint buttons,bool escape){if(buttons!=0)previewInjected.Add(1);if(escape)previewInjected.Add(27);using(var self=PinnedProcessOwner.PinCurrent()){string path=Path.Combine(output,"input-owed.json"),temporary=path+".tmp";File.WriteAllText(temporary,new JObject{{"pid",self.Id},{"creation_filetime",self.CreationFileTime},{"buttons",buttons},{"keys",escape?new JArray(27):new JArray()},{"injected",new JArray(previewInjected)}}.ToString());if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}}
 async Task NativePreviewGesture(bool drag){
  Need(new[]{1,16,17,18,27,91,92}.All(key=>(GetAsyncKeyState(key)&0x8000)==0),"preview QA requires clear physical primary/Escape/modifiers");
  var window=new IntPtr((long)library.NativePreviewState["window"]);Need(GetClientRect(window,out var bounds),"native child client rectangle");var point=new Point{X=(bounds.Right-bounds.Left)/2,Y=(bounds.Bottom-bounds.Top)/2};Need(ClientToScreen(window,ref point),"native child actual physical centre");var parent=WindowsDesktopWindow.FindOwnWindow();uint current=GetCurrentThreadId(),foreground=GetWindowThreadProcessId(GetForegroundWindow(),out var ignored);bool attached=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true);try{SetForegroundWindow(parent);}finally{if(attached)Need(AttachThreadInput(current,foreground,false),"preview QA foreground queues detached");}Need(SetCursorPos(point.X,point.Y),"preview QA owned pointer positioned");await Task.Delay(100);bool pointer=GetCursorPos(out var actual);report["native_gesture_focus"]=new JObject{{"pointer_available",pointer},{"pointer",new JArray(actual.X,actual.Y)},{"expected_pointer",new JArray(point.X,point.Y)},{"hit",WindowFromPoint(actual).ToInt64()},{"child",window.ToInt64()},{"foreground",GetForegroundWindow().ToInt64()},{"parent",parent.ToInt64()},{"held",new JArray(new[]{1,16,17,18,27,91,92}.Where(key=>(GetAsyncKeyState(key)&0x8000)!=0))}};Need(pointer&&WindowFromPoint(actual)==window&&GetForegroundWindow()==parent&&actual.X==point.X&&actual.Y==point.Y,"preview QA real own hit/foreground/centre before exact gesture");PreviewLedger(4,false);
  double beforeX=(double)library.NativePreviewState["preview_ui"]["drag_x"],beforeY=(double)library.NativePreviewState["preview_ui"]["drag_y"];
  try{mouse_event(2,0,0,0,UIntPtr.Zero);await Until(()=>(bool?)library.NativePreviewState?["preview_ui"]?["dragging"]==true,"owned primary down reached trusted native UI",2000);if(drag)for(int step=1;step<=6;step++){SetCursorPos(point.X+step*8,point.Y+step*3);double expectedX=beforeX+step*8.0/bounds.Right,expectedY=beforeY-step*3.0/bounds.Bottom;await Until(()=>Math.Abs((double)library.NativePreviewState["preview_ui"]["drag_x"]-expectedX)<1.5/bounds.Right&&Math.Abs((double)library.NativePreviewState["preview_ui"]["drag_y"]-expectedY)<1.5/bounds.Bottom,"exact owned normalized native drag step "+step,2000);}}
  finally{mouse_event(4,0,0,0,UIntPtr.Zero);PreviewLedger(0,false);}
 }
 async Task NativePreviewEscape(){
  await NativePreviewGesture(false);PreviewLedger(0,true);try{keybd_event(27,0,0,UIntPtr.Zero);await Task.Delay(40);}finally{keybd_event(27,0,2,UIntPtr.Zero);PreviewLedger(0,false);}
 }
 async Task RunNativePreview(string target,string preset){
  await Apply(ArtworkCatalog.Find("pool"),target,preset);int runtimePid=(int)manager.Client.State["pid"];
  Select(ArtworkCatalog.Find("showcase"),target,preset);await Task.Delay(100);Click("预览");await Until(()=>!library.Loading,"original native Library preview ready");Need(library.Loaded?.Id=="showcase"&&library.Error==null,"original native preview: "+library.Error);
  var owner=Field<NativePreviewTarget>(library,"nativePreview");Need(owner!=null&&owner.Verify(),"original Library owns verified native child");var state=library.NativePreviewState;Need((int)state["pid"]!=runtimePid&&(int)state["pid"]!=System.Diagnostics.Process.GetCurrentProcess().Id&&!runtime.CameraStarted,"native preview must not reuse manager or desktop renderer/Tracker");var display=WindowsDesktopWindow.Displays(true).Single(item=>item.Id==runtime.DisplayId);Need((int)state["v_sync_count"]==display.NativeVSyncCount,"native Library uses actual per-target automatic presentation policy");report["native_display_refresh"]=new JObject{{"numerator",display.RefreshNumerator},{"denominator",display.RefreshDenominator},{"expected_sync",display.NativeVSyncCount}};report["native_original_preview"]=state;Passed("original-Library-native-child-real-crop-and-sole-Tracker");await Shot("native-original-preview");
  var before=library.MouseEye;await NativePreviewGesture(true);await Until(()=>(library.MouseEye-before).sqrMagnitude>.00001f,"real native preview drag reaches original MouseEye");await Until(()=>Math.Abs((float)library.NativePreviewState["eye"][0]-library.MouseEye.x*runtime.projection.displayWidth)<.0001f,"original MouseEye returns asynchronously to native renderer");report["native_dragged_preview"]=library.NativePreviewState;await Shot("native-original-preview-dragged");
  Click("复位视角");Need(library.MouseEye==Vector2.zero,"native reset original eye");Click("摄像头");await Task.Delay(200);Need(library.CameraMode&&!runtime.CameraStarted,"native camera preview duplicated Tracker");Click("鼠标观察");await Task.Delay(100);Click("保左右");Need(!library.KeepHeight,"native framing controls");await Task.Delay(100);Click("保上下");await Task.Delay(100);Passed("native-preview-original-drag-reset-camera-framing");
  Click("全屏预览");await Until(()=>Screen.fullScreen&&(int?)library.NativePreviewState?["screen_width"]==Screen.width,"native fullscreen actual client resolution");await Shot("native-original-fullscreen");await NativePreviewEscape();await Until(()=>!Screen.fullScreen&&library.Loaded!=null,"native child Escape exits original fullscreen");Passed("native-preview-real-Escape-restores-original-preview-page");
  await NativePreviewEscape();await Until(()=>!library.Loading&&library.Loaded==null,"native child Escape closes original preview");Need(owner.ResourcesReleased&&Field<NativePreviewTarget>(library,"nativePreview")==null,"native child resources before desktop resume");await manager.Client.Refresh();Need((bool?)manager.Client.State["preview"]==false&&manager.Client.ActiveArtworkId=="pool","native preview restored exact old desktop item");Passed("native-preview-retired-before-exact-desktop-resume");await Shot("native-preview-returned-library");
 }
}

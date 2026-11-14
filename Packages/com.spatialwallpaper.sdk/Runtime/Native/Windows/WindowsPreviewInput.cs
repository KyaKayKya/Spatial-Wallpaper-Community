using System;using System.Collections.Generic;using System.ComponentModel;using System.Runtime.InteropServices;using System.Text;
using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk.Editor.Native {
 // Trusted dedicated preview surface only. Never attach this to Explorer, a
 // Worker HWND. A host containing controls requires a trusted content-region
 // proof on every sample; never use its whole client area. Desktop acquisition has
 // a separate hit-test/focus contract and cannot use this direct-click adapter.
 public sealed class WindowsPreviewInput:IDisposable {
  public delegate bool RegionProof(bool requireFocus,out int left,out int top,out int width,out int height);
  const uint Id=0x53574931;static readonly HashSet<IntPtr> attached=new HashSet<IntPtr>();
  readonly IntPtr window;readonly uint thread;readonly IPreviewInputTarget target;readonly Procedure callback;readonly byte[] keys=new byte[256];
  readonly StringBuilder desktopName=new StringBuilder(128);long sequence;bool closed,registered,session=true;uint committedHeld;ulong committedEpoch;
  readonly RegionProof region;readonly Action focusContent,escape;
  readonly Queue<string> messageTrace;
  public string[] MessageTrace=>messageTrace?.ToArray()??new string[0];
  string proofFailure,proofDetail;
  public string SourceRevocationDetail{get;private set;}
  public long SourceRevocationSequence{get;private set;}
  public long SourceRevocationTime{get;private set;}
  public string SourceRevocationReason{get;private set;}
  void RevokeSource(string reason){if(reason!="region-unavailable"){SourceRevocationSequence=checked(SourceRevocationSequence+1);SourceRevocationTime=WindowsMachineClock.Milliseconds();SourceRevocationReason=reason;SourceRevocationDetail=reason=="os-focus-or-session"?proofDetail:null;}target.Revoke();}
  [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Gui {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretRect;}
  delegate IntPtr Procedure(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
  delegate bool WindowVisitor(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,WindowVisitor callback,IntPtr data);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("comctl32.dll",SetLastError=true)]static extern bool SetWindowSubclass(IntPtr hwnd,Procedure proc,UIntPtr id,UIntPtr data);
  [DllImport("comctl32.dll")]static extern bool RemoveWindowSubclass(IntPtr hwnd,Procedure proc,UIntPtr id);
  [DllImport("comctl32.dll")]static extern IntPtr DefSubclassProc(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll",SetLastError=true)]static extern UIntPtr SetTimer(IntPtr hwnd,UIntPtr id,uint milliseconds,IntPtr callback);
  [DllImport("user32.dll")]static extern bool KillTimer(IntPtr hwnd,UIntPtr id);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")]static extern uint GetCurrentProcessId();
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref Gui info);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern bool ScreenToClient(IntPtr hwnd,ref Point point);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")]static extern IntPtr SetFocus(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetKeyboardState(byte[] values);
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")]static extern int GetMessageTime();
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
  [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
  [DllImport("user32.dll")]static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder value,uint length,out uint needed);
  [DllImport("wtsapi32.dll",SetLastError=true)]static extern bool WTSRegisterSessionNotification(IntPtr hwnd,uint flags);
  [DllImport("wtsapi32.dll")]static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
  static readonly int[] buttons={1,2,32,37,39,38,40};
  public WindowsPreviewInput(IntPtr hwnd,IPreviewInputTarget input):this(hwnd,input,null,null){}
  public WindowsPreviewInput(IntPtr hwnd,IPreviewInputTarget input,RegionProof contentRegion,Action acquireContentFocus,Action trustedEscape=null,bool traceMessages=false){
   region=contentRegion;focusContent=acquireContentFocus;escape=trustedEscape;
   if(traceMessages)messageTrace=new Queue<string>();
   target=input??throw new ArgumentNullException(nameof(input));window=hwnd;thread=GetWindowThreadProcessId(hwnd,out var process);
   if(hwnd==IntPtr.Zero||process!=GetCurrentProcessId()||thread!=GetCurrentThreadId())throw new ArgumentException("preview-input-owned-ui-thread");
   lock(attached)if(!attached.Add(hwnd))throw new InvalidOperationException("preview-input-already-attached");
   callback=Dispatch;
   try{
    if(!DesktopActive())throw new InvalidOperationException("preview-input-interactive-desktop-required");
    if(!SetWindowSubclass(window,callback,(UIntPtr)Id,UIntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
    if(!WTSRegisterSessionNotification(window,0))throw new Win32Exception(Marshal.GetLastWin32Error());registered=true;
    if(SetTimer(window,(UIntPtr)Id,16,IntPtr.Zero)==UIntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
    target.Revoke();
   }catch{Dispose();throw;}
  }
  bool Named(IntPtr handle,string expected){desktopName.Clear();return handle!=IntPtr.Zero&&GetUserObjectInformation(handle,2,desktopName,256,out var needed)&&string.Equals(desktopName.ToString(),expected,StringComparison.OrdinalIgnoreCase);}
  bool DesktopActive(){
   if(!session||!Named(GetProcessWindowStation(),"WinSta0"))return false;
   IntPtr desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return false;
   try{return Named(desktop,"Default");}finally{CloseDesktop(desktop);}
  }
  bool Proof(bool focus,out float x,out float y){
   x=y=0;IntPtr root=GetAncestor(window,2);var gui=new Gui{Size=(uint)Marshal.SizeOf(typeof(Gui))};
   proofFailure="os-focus-or-session";
   if(!IsWindowVisible(window)){proofDetail="hidden";return false;}if(!IsWindowEnabled(window)){proofDetail="disabled";return false;}if(GetForegroundWindow()!=root){proofDetail="foreground";return false;}if(!GetGUIThreadInfo(thread,ref gui)){proofDetail="gui-read";return false;}if(gui.Active!=root){proofDetail="gui-active:"+gui.Active+"/"+root;return false;}if((gui.Flags&30)!=0){proofDetail="gui-flags:"+gui.Flags;return false;}if(focus&&gui.Focus!=window){proofDetail="gui-focus:"+gui.Focus+"/"+window;return false;}if(!DesktopActive()){proofDetail="input-desktop";return false;}
   // Managed/custom popup menus may not set GUI_INMENUMODE or move native
   // keyboard focus. Treat every visible popup on this UI thread as exclusion.
   bool popup=false;bool enumerated=EnumThreadWindows(thread,(candidate,data)=>{if(candidate!=root&&IsWindowVisible(candidate)&&((uint)GetWindowLong(candidate,-16)&0x80000000u)!=0){popup=true;return false;}return true;},IntPtr.Zero);proofFailure="thread-popup";if(!enumerated||popup)return false;
   proofFailure="pointer-window";if(!GetCursorPos(out var point)||WindowFromPoint(point)!=window)return false;
   if(region!=null){proofFailure="region-unavailable";if(!region(focus,out int left,out int top,out int w,out int h)||w<=0||h<=0)return false;proofFailure="crop";if(point.X<left||point.Y<top||(long)point.X-left>=w||(long)point.Y-top>=h)return false;x=(point.X-left)/(float)w;y=(point.Y-top)/(float)h;return true;}
   proofFailure="client-region";
   if(!ScreenToClient(window,ref point)||!GetClientRect(window,out var rect))return false;
   int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
   if(width<=0||height<=0||point.X<0||point.Y<0||point.X>=width||point.Y>=height)return false;
   x=point.X/(float)width;y=point.Y/(float)height;return true;
  }
  void Sample(uint message,IntPtr w,bool heartbeat,bool escapeEdge=false){
   if(closed)return;ulong epoch=target.CaptureEpoch;long now=WindowsMachineClock.Milliseconds();
   // Message time is a wrapping 32-bit tick count; preserve age rather than
   // replacing queued input time with the moment of dispatch.
   long time=heartbeat?now:now-unchecked((int)((uint)now-(uint)GetMessageTime()));
   bool primary=message==0x201||message==0x203;
   if(primary&&Proof(false,out var ignoredX,out var ignoredY)){focusContent?.Invoke();SetFocus(window);}
   if(!Proof(true,out var x,out var y)){RevokeSource(proofFailure);return;}
   uint held=0;bool modifier=false;
   if(heartbeat){
    // Async key state can already contain a key whose native window message
    // has not been dispatched. Renew only previously committed raw state;
    // modifiers/focus/region still use current OS evidence to revoke.
    held=committedEpoch==epoch?committedHeld:0;
    foreach(int key in new[]{16,17,18,91,92})modifier|=(GetAsyncKeyState(key)&0x8000)!=0;
   }
   else{
    if(!GetKeyboardState(keys)){RevokeSource("keyboard-state");return;}
    for(int i=0;i<buttons.Length;i++)if((keys[buttons[i]]&128)!=0)held|=1u<<i;
    modifier=(keys[16]&128)!=0||(keys[17]&128)!=0||(keys[18]&128)!=0||(keys[91]&128)!=0||(keys[92]&128)!=0;
   }
   if(escapeEdge&&!modifier)escape?.Invoke();
   int wheel=message==0x20a?unchecked((short)((long)w>>16)):0;
   if(sequence==long.MaxValue){Dispose();return;}
   target.Observe(new InputObservation(++sequence,time,true,true,true,true,true,modifier,primary,held,x,y,wheel,epoch,heartbeat));
   if(!heartbeat){committedHeld=held;committedEpoch=target.CaptureEpoch;}
  }
  IntPtr Dispatch(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data){
   try{
    if(messageTrace!=null&&(message==6||message==7||message==8||message==0x21||message==0x86||message>=0x201&&message<=0x203)){var gui=new Gui{Size=(uint)Marshal.SizeOf(typeof(Gui))};GetGUIThreadInfo(thread,ref gui);if(messageTrace.Count==32)messageTrace.Dequeue();messageTrace.Enqueue(WindowsMachineClock.Milliseconds()+":"+message.ToString("x")+":"+w+":"+l+":foreground="+GetForegroundWindow()+":active="+gui.Active+":focus="+gui.Focus);}
    if(message==0x82){Dispose();return DefSubclassProc(hwnd,message,w,l);}
    if(message==0x2b1){int change=w.ToInt32();session=change==1||change==3||change==5||change==8;RevokeSource("session-message");}
    // Releasing our own mouse capture after button-up is not loss of keyboard
    // focus. Transfer to another capture owner is; normal release is rechecked
    // by the next native proof/heartbeat without discarding the click edges.
    else if(message==3||message==5||message==8||message==0x1f||message==0x211||message==0x215&&l!=IntPtr.Zero&&l!=window||message==0x18&&w==IntPtr.Zero)RevokeSource("native-message-"+message.ToString("x"));
    else if(message==0x113&&w.ToInt64()==Id)Sample(message,w,true);
    else if(message==0x100||message==0x101||message==0x104||message==0x105||message>=0x200&&message<=0x20a)Sample(message,w,false,message==0x100&&w.ToInt64()==27&&(l.ToInt64()&0x40000000)==0);
   }catch{Dispose();}
   return DefSubclassProc(hwnd,message,w,l);
  }
  public void Dispose(){
   if(closed)return;if(GetCurrentThreadId()!=thread)throw new InvalidOperationException("preview-input-dispose-ui-thread");closed=true;
   // Transport failure must never escape a native callback or prevent removing
   // the callback/delegate root. The channel closes itself on failure; Host
   // revokes on disconnect and independently expires old observations.
   try{target.Revoke();}catch{}
   finally{KillTimer(window,(UIntPtr)Id);RemoveWindowSubclass(window,callback,(UIntPtr)Id);if(registered)WTSUnRegisterSessionNotification(window);registered=false;lock(attached)attached.Remove(window);}
  }
 }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
namespace SpatialWallpaper.Content {
 // Native shell WM_DROPFILES is the real Windows file-drop entry point.
 // The delegate stays rooted until the original window procedure is restored.
 public sealed class PackageDropReceiver:MonoBehaviour {
  public Action<string[]> Dropped;public Action<string> Error;
  IntPtr window,original;WindowProc callback;readonly Queue<string[]> pending=new Queue<string[]>();volatile string pendingError;volatile bool closeRequested;
  delegate IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc visit,IntPtr data);delegate bool EnumProc(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr address);
  [DllImport("user32.dll")]static extern IntPtr CallWindowProc(IntPtr previous,IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
  [DllImport("shell32.dll")]static extern void DragAcceptFiles(IntPtr hwnd,bool accept);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern uint DragQueryFile(IntPtr drop,uint index,StringBuilder name,uint count);
  [DllImport("shell32.dll")]static extern void DragFinish(IntPtr drop);
  void Update(){
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
   // Quit outside the reverse P/Invoke callback. Unity can tear down Mono while
   // handling WM_CLOSE; forwarding it with a managed callback still on the
   // native stack crashes the Manager during an otherwise normal close.
   if(closeRequested){Detach();Application.Quit();return;}
   if(window==IntPtr.Zero){uint own=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;EnumWindows((hwnd,_)=>{GetWindowThreadProcessId(hwnd,out uint pid);if(pid!=own)return true;var name=new StringBuilder(64);GetClassName(hwnd,name,64);if(name.ToString()!="UnityWndClass")return true;window=hwnd;return false;},IntPtr.Zero);if(window!=IntPtr.Zero){callback=Receive;original=SetWindowLongPtr(window,-4,Marshal.GetFunctionPointerForDelegate(callback));if(original==IntPtr.Zero){Error?.Invoke("无法注册文件拖入，请重启后重试");window=IntPtr.Zero;}else DragAcceptFiles(window,true);}}
   if(pendingError!=null){string error=pendingError;pendingError=null;Error?.Invoke(error);}while(true){string[] paths;lock(pending){if(pending.Count==0)break;paths=pending.Dequeue();}Dropped?.Invoke(paths);}
#endif
  }
  IntPtr Receive(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam){
   if(message==0x10){closeRequested=true;return IntPtr.Zero;}
   if(message!=0x233)return CallWindowProc(original,hwnd,message,wParam,lParam);
   try{uint count=DragQueryFile(wParam,uint.MaxValue,null,0);if(count<1||count>16)throw new InvalidOperationException("每次请拖入1至16个作品包");var paths=new string[count];for(uint i=0;i<count;i++){uint size=DragQueryFile(wParam,i,null,0);if(size>32767)throw new InvalidOperationException("文件路径过长");var path=new StringBuilder((int)size+1);DragQueryFile(wParam,i,path,(uint)path.Capacity);paths[i]=path.ToString();}lock(pending){if(pending.Count>=8)throw new InvalidOperationException("拖入队列已满，请稍后重试");pending.Enqueue(paths);}}catch(Exception e){pendingError=e.Message;}finally{DragFinish(wParam);}return IntPtr.Zero;
  }
  void Detach(){if(window!=IntPtr.Zero&&original!=IntPtr.Zero){DragAcceptFiles(window,false);SetWindowLongPtr(window,-4,original);}window=original=IntPtr.Zero;callback=null;}
  void OnDestroy(){Detach();}
 }
}

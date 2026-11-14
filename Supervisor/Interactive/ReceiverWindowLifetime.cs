using System;using System.Collections.Generic;using System.ComponentModel;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace SpatialWallpaper.Supervision.Interactive {
 // Captures one HWND lifetime before asynchronous connection starts. A reused
 // numeric HWND cannot clear Destroyed. The native callback has a managed root.
 internal sealed class ReceiverWindowLifetime:IDisposable {
  delegate IntPtr Subclass(IntPtr window,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
  static readonly HashSet<ReceiverWindowLifetime> roots=new HashSet<ReceiverWindowLifetime>();
  static int nextId;
  readonly IntPtr window;readonly uint thread;readonly UIntPtr id;readonly Subclass callback;
  readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  bool registered;
  Func<bool> removeSubclass=null;
  public bool Destroyed{get;private set;}public bool Released=>!registered;public Task Completion=>completion.Task;public int RemovalError{get;private set;}
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")]static extern uint GetCurrentProcessId();
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("comctl32.dll",SetLastError=true)]static extern bool SetWindowSubclass(IntPtr window,Subclass callback,UIntPtr id,UIntPtr data);
  [DllImport("comctl32.dll",SetLastError=true)]static extern bool RemoveWindowSubclass(IntPtr window,Subclass callback,UIntPtr id);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
  [DllImport("comctl32.dll")]static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr w,IntPtr l);
  public ReceiverWindowLifetime(IntPtr window){
   thread=GetWindowThreadProcessId(window,out uint process);
   if(window==IntPtr.Zero||thread!=GetCurrentThreadId()||process!=GetCurrentProcessId())throw new ArgumentException("input-receiver-owned-ui-thread");
   this.window=window;lock(roots){nextId=checked(nextId+1);id=new UIntPtr((uint)nextId);}callback=Dispatch;
   lock(roots)roots.Add(this);
   if(!SetWindowSubclass(window,callback,id,UIntPtr.Zero)){int error=Marshal.GetLastWin32Error();lock(roots)roots.Remove(this);throw new Win32Exception(error);}
   registered=true;
  }
  public void Owner(){if(GetCurrentThreadId()!=thread)throw new InvalidOperationException("input-receiver-owning-ui-thread");}
  IntPtr Dispatch(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr ignored,UIntPtr data){
   if(message!=0x82)return DefSubclassProc(hwnd,message,w,l);
   Destroyed=true;
   bool removed=Remove();int error=removed?0:Marshal.GetLastWin32Error();
   try{return DefSubclassProc(hwnd,message,w,l);}
   finally{if(removed){registered=false;lock(roots)roots.Remove(this);completion.TrySetResult(true);}else{RemovalError=error==0?31:error;completion.TrySetException(new Win32Exception(RemovalError));}}
  }
  // If removal failed during destruction, retain the callback until a later
  // owner-thread observation proves the HWND is gone. A recycled live numeric
  // handle is inconclusive and must not release the retained callback root.
  bool Remove(){if(removeSubclass!=null)return removeSubclass();if(RemoveWindowSubclass(window,callback,id))return true;return Destroyed&&!IsWindow(window);}
  public void Dispose(){Owner();if(!registered)return;
   if(!Remove()){RemovalError=Marshal.GetLastWin32Error();if(RemovalError==0)RemovalError=31;completion.TrySetException(new Win32Exception(RemovalError));return;}
   registered=false;lock(roots)roots.Remove(this);completion.TrySetResult(true);
  }
 }
}

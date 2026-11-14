using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Desktop {
 public sealed class NativePreviewTarget {
  [StructLayout(LayoutKind.Sequential)]struct Bounds {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Position {public IntPtr Window,After;public int X,Y,Width,Height;public uint Flags;}
  delegate IntPtr Procedure(IntPtr window,uint message,IntPtr first,IntPtr second,UIntPtr id,UIntPtr data);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Bounds bounds);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
  [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
  [DllImport("user32.dll")]static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetProp(IntPtr window,string name,IntPtr value);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetProp(IntPtr window,string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr RemoveProp(IntPtr window,string name);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr window,IntPtr parent);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetStyle(IntPtr window,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetStyle(IntPtr window,int index,IntPtr style);
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
  [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("comctl32.dll",SetLastError=true)]static extern bool SetWindowSubclass(IntPtr window,Procedure procedure,UIntPtr id,UIntPtr data);
  [DllImport("comctl32.dll")]static extern bool RemoveWindowSubclass(IntPtr window,Procedure procedure,UIntPtr id);
  [DllImport("comctl32.dll")]static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr first,IntPtr second);
  static long nextHook;
  readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
  readonly uint nativeThread=GetCurrentThreadId(),parentPid=(uint)Process.GetCurrentProcess().Id;
  readonly IntPtr parent;readonly NativeRenderProcess process;readonly Procedure callback;readonly UIntPtr hookId;
  readonly bool traceFocus;
  public string FirstParentFocusStack{get;private set;}
  public long FirstParentFocusTime{get;private set;}
  public void ResetFocusTrace(){Owner();FirstParentFocusStack=null;FirstParentFocusTime=0;}
  readonly string lifetimeProperty="SpatialWallpaper.NativePreview."+Guid.NewGuid().ToString("N");readonly string parentLifetimeProperty;readonly IntPtr lifetimeValue=new IntPtr(1);
  GCHandle callbackRoot;
  PinnedProcessOwner pin;NativeRenderSurface surface;IntPtr window,originalParent,style,extended;Bounds originalBounds;
  Task closing,work=Task.CompletedTask;bool hooked,attached,tagged,saved,parentTagged,geometryInvalid=true,workObserved=true;volatile bool parentEnded,stoppingWindow;int windowPhase,failurePhase=-1;int[] region,desiredRegion,workingRegion;Point desiredPoint;long version,workingVersion,measuredVersion,displayRevision;string windowError;JObject requestedPose;
  public Task<NativeRenderSurface> Ready=>process.Ready;
  public JObject State=>process.State;
  public string Error=>Volatile.Read(ref windowError)??process.Error;
  public string CleanupError=>process.CleanupError;
  public long DisplayRevision=>displayRevision;
  public bool ResourcesReleased=>closing!=null&&closing.IsCompleted&&process.ResourcesReleased&&pin==null&&window==IntPtr.Zero&&!hooked&&!parentTagged;
  public NativePreviewTarget(string player,string cache,IntPtr ownedParent,JObject configuration){
   traceFocus=(bool?)configuration?["input_trace"]==true;
   parent=ownedParent;parentLifetimeProperty=lifetimeProperty+".parent";RequireParent();if((bool?)configuration?["desktop"]!=false)throw new ArgumentException("native-preview-role");
   requestedPose=(JObject)configuration["pose"].DeepClone();var prepared=(JObject)configuration.DeepClone();prepared["pose"]["paused"]=true;
   hookId=(UIntPtr)checked((ulong)Interlocked.Increment(ref nextHook));callback=ParentMessage;
   if(!SetProp(parent,parentLifetimeProperty,lifetimeValue))throw new Win32Exception();parentTagged=true;callbackRoot=GCHandle.Alloc(this);if(!SetWindowSubclass(parent,callback,hookId,UIntPtr.Zero)){callbackRoot.Free();RemoveProp(parent,parentLifetimeProperty);parentTagged=false;throw new Win32Exception(Marshal.GetLastWin32Error());}hooked=true;
   try{process=new NativeRenderProcess(player,cache,prepared);}catch{if(RemoveWindowSubclass(parent,callback,hookId)){hooked=false;callbackRoot.Free();RemoveProp(parent,parentLifetimeProperty);parentTagged=false;}throw;}
  }
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("native-preview-owner-thread");}
  void RequireParent(){if(parentEnded||parent==IntPtr.Zero||!IsWindow(parent)||GetWindowThreadProcessId(parent,out uint pid)!=nativeThread||pid!=parentPid||parentTagged&&GetProp(parent,parentLifetimeProperty)!=lifetimeValue)throw new ArgumentException("native-preview-owned-parent-thread");}
  void PlacementLive(){if(stoppingWindow)throw new OperationCanceledException("native-preview-placement-retired");if(parentEnded||!IsWindow(parent)||GetWindowThreadProcessId(parent,out uint pid)!=nativeThread||pid!=parentPid||GetProp(parent,parentLifetimeProperty)!=lifetimeValue)throw new InvalidOperationException("native-preview-parent-lifetime");}
  bool Matches()=>surface!=null&&WindowsDesktopWindow.MatchesPinnedChildWindow(window,pin,surface.RenderThread)&&(!tagged||GetProp(window,lifetimeProperty)==lifetimeValue);
  JObject EffectivePose(){var value=(JObject)requestedPose.DeepClone();if(region!=null)value["input_region"]=new JArray(region);if(geometryInvalid||!attached)value["paused"]=true;return value;}
  void Invalidate(){geometryInvalid=true;version=checked(version+1);if(window!=IntPtr.Zero&&Matches())ShowWindowAsync(window,0);process?.PublishPose(EffectivePose());}
  void WindowFailure(Exception failure){stoppingWindow=true;Interlocked.CompareExchange(ref failurePhase,Volatile.Read(ref windowPhase),-1);Interlocked.CompareExchange(ref windowError,"native-preview-window:"+failure.GetType().Name+":"+failure.Message,null);process.Stop();}
  IntPtr ParentMessage(IntPtr handle,uint message,IntPtr first,IntPtr second,UIntPtr id,UIntPtr data){
   try{
    if(traceFocus&&message==7&&first==window&&window!=IntPtr.Zero&&FirstParentFocusStack==null){FirstParentFocusTime=SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();FirstParentFocusStack=Environment.StackTrace;}
    bool changing=message==0x46&&second!=IntPtr.Zero&&(Marshal.PtrToStructure<Position>(second).Flags&3)!=3;
    if(message==0x7e)displayRevision=checked(displayRevision+1);
    if(changing||message==3||message==5||message==0x2e0||message==0x7e||message==0x231||message==0x18&&first==IntPtr.Zero)Invalidate();
    if(message==0x82){parentEnded=stoppingWindow=true;parentTagged=false;Invalidate();if(RemoveWindowSubclass(parent,callback,hookId)){hooked=false;if(callbackRoot.IsAllocated)callbackRoot.Free();}process?.Stop();}
   }catch{stoppingWindow=true;geometryInvalid=true;process?.Stop();}
   return DefSubclassProc(handle,message,first,second);
  }
  Point ValidateRegion(int[] value){
   RequireParent();if(value==null||value.Length!=4||value[2]<=0||value[3]<=0||value[2]>8192||value[3]>8192)throw new ArgumentException("native-preview-region");
   var origin=new Point();if(!GetClientRect(parent,out var client)||!ClientToScreen(parent,ref origin))throw new Win32Exception();
   long x=(long)value[0]-origin.X,y=(long)value[1]-origin.Y;if(x<0||y<0||x+value[2]>client.Right-client.Left||y+value[3]>client.Bottom-client.Top)throw new ArgumentException("native-preview-region-outside-parent");return new Point{X=(int)x,Y=(int)y};
  }
  public void AttachReady(int[] physicalRegion){
   Owner();if(closing!=null)throw new InvalidOperationException("native-preview-closing");if(Ready.Status!=TaskStatus.RanToCompletion)throw new InvalidOperationException("native-preview-not-ready");
   if(surface==null){surface=Ready.GetAwaiter().GetResult();pin=surface.Pin();window=surface.Window;}
   SetRegion(physicalRegion);
  }
  public void SetRegion(int[] physicalRegion){
   Owner();if(closing!=null)throw new InvalidOperationException("native-preview-closing");if(surface==null)throw new InvalidOperationException("native-preview-not-ready");if(Error!=null)throw new InvalidOperationException(Error);
   IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{
    var point=ValidateRegion(physicalRegion);bool changed=desiredRegion==null||desiredPoint.X!=point.X||desiredPoint.Y!=point.Y;for(int index=0;!changed&&index<4;index++)changed=desiredRegion[index]!=physicalRegion[index];
    if(changed){desiredRegion=(int[])physicalRegion.Clone();desiredPoint=point;Invalidate();}measuredVersion=version;ObserveWork();if(Error!=null)throw new InvalidOperationException(Error);if(!work.IsCompleted)return;if(!changed&&!geometryInvalid)return;
    BeginDesiredPlacement();
   }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  void BeginDesiredPlacement(){workingRegion=(int[])desiredRegion.Clone();workingVersion=version;var placement=workingRegion;var location=desiredPoint;workObserved=false;work=Task.Run(()=>Place(location,placement));WatchWindowWork(work);}
  void Place(Point point,int[] placement){
   IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{
    PlacementLive();if(!Matches())throw new InvalidOperationException("native-preview-surface-ended");
    if(!attached){
     originalParent=GetParent(window);style=GetStyle(window,-16);extended=GetStyle(window,-20);if(!GetWindowRect(window,out originalBounds))throw new Win32Exception();saved=true;
     if(!SetProp(window,lifetimeProperty,lifetimeValue))throw new Win32Exception();tagged=true;ShowWindowAsync(window,0);
     long child=(style.ToInt64()&~0x90cf0000L)|0x44000000L;PlacementLive();Volatile.Write(ref windowPhase,1);SetStyle(window,-16,new IntPtr(child));PlacementLive();SetStyle(window,-20,new IntPtr(extended.ToInt64()&~0x40000L));PlacementLive();
     Volatile.Write(ref windowPhase,2);SetParent(window,parent);PlacementLive();if(GetParent(window)!=parent||GetStyle(window,-16).ToInt64()!=child)throw new Win32Exception(Marshal.GetLastWin32Error(),"native-preview-attach-failed");attached=true;
    }
    PlacementLive();if(!Matches()||GetParent(window)!=parent)throw new InvalidOperationException("native-preview-placement-identity");
    Volatile.Write(ref windowPhase,3);if(!SetWindowPos(window,IntPtr.Zero,point.X,point.Y,placement[2],placement[3],0x0010|0x0020|0x0004))throw new Win32Exception();PlacementLive();
    if(!GetClientRect(window,out var client)||client.Right-client.Left!=placement[2]||client.Bottom-client.Top!=placement[3])throw new InvalidOperationException("native-preview-placement-unconfirmed");
   }catch(OperationCanceledException)when(stoppingWindow){}catch(Exception failure){WindowFailure(failure);throw;}finally{Volatile.Write(ref windowPhase,0);if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  async void WatchWindowWork(Task operation){try{if(await Task.WhenAny(operation,Task.Delay(2000)).ConfigureAwait(false)!=operation)WindowFailure(new TimeoutException("native-preview-window-deadline"));}catch(Exception failure){WindowFailure(failure);}}
  void ObserveWork(){
   if(workObserved||!work.IsCompleted)return;workObserved=true;
   if(work.IsFaulted){var observed=work.Exception;return;}if(closing!=null||Error!=null||parentEnded)return;if(workingVersion!=version){if(measuredVersion==version&&desiredRegion!=null)BeginDesiredPlacement();return;}
   region=(int[])workingRegion.Clone();geometryInvalid=false;if(!ShowWindowAsync(window,4)){Invalidate();WindowFailure(new Win32Exception());return;}process.PublishPose(EffectivePose());
  }
  public bool Verify(){Owner();ObserveWork();return closing==null&&Error==null&&attached&&!geometryInvalid&&Matches()&&GetParent(window)==parent;}
  public void Suspend(){Owner();if(closing==null&&!geometryInvalid)Invalidate();}
  public void PublishPose(JObject value){Owner();if(value==null)throw new ArgumentNullException(nameof(value));if(closing!=null)return;requestedPose=(JObject)value.DeepClone();process.PublishPose(EffectivePose());}
  public Task CloseAsync(){
   Owner();if(closing!=null){if(closing.IsCompleted)RemoveParentHook();return closing;}stoppingWindow=true;Invalidate();RemoveParentHook();closing=ReleaseAsync();return closing;
  }
  void RemoveParentHook(){if(hooked){if(!parentEnded&&IsWindow(parent)&&!RemoveWindowSubclass(parent,callback,hookId)){Interlocked.CompareExchange(ref windowError,"native-preview-parent-hook-release",null);return;}hooked=false;if(callbackRoot.IsAllocated)callbackRoot.Free();}if(parentTagged){if(!parentEnded&&GetProp(parent,parentLifetimeProperty)==lifetimeValue)RemoveProp(parent,parentLifetimeProperty);parentTagged=false;}}
  void Detach(){
   if(window==IntPtr.Zero)return;IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{
    if(!Matches()){if(pin!=null&&!pin.HasExited&&IsWindow(window))throw new InvalidOperationException("native-preview-detach-identity-unproven");return;}
    if(!saved)return;
    var restore=IsWindow(originalParent)?originalParent:IntPtr.Zero;SetParent(window,restore);if(GetAncestor(window,1)!=(restore==IntPtr.Zero?GetDesktopWindow():restore))throw new Win32Exception(Marshal.GetLastWin32Error(),"native-preview-detach-failed");
    SetStyle(window,-16,style);SetStyle(window,-20,extended);if(GetStyle(window,-16)!=style||GetStyle(window,-20)!=extended||!SetWindowPos(window,IntPtr.Zero,originalBounds.Left,originalBounds.Top,originalBounds.Right-originalBounds.Left,originalBounds.Bottom-originalBounds.Top,0x0010|0x0020|0x0004))throw new Win32Exception(Marshal.GetLastWin32Error(),"native-preview-restore-failed");
    if(tagged&&RemoveProp(window,lifetimeProperty)!=lifetimeValue)throw new InvalidOperationException("native-preview-window-cookie-release");
   }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  async Task ReleaseAsync(){
   Exception failure=null;try{await work.ConfigureAwait(false);}catch(Exception error){failure=error;}
   try{var detach=Task.Run(Detach);WatchWindowWork(detach);await detach.ConfigureAwait(false);}catch(Exception error){WindowFailure(error);failure=failure==null?error:new AggregateException(failure,error);}
   process.Stop();try{await process.Completion.ConfigureAwait(false);}catch(Exception error){failure=failure==null?error:new AggregateException(failure,error);}
   finally{if(process.ResourcesReleased){pin?.Dispose();pin=null;window=IntPtr.Zero;attached=tagged=false;}}
   if(failure!=null)throw failure;if(windowError!=null)throw new InvalidOperationException(windowError);
  }
 }
}

using System;using System.ComponentModel;using System.Runtime.InteropServices;using System.Text;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Supervision.Interactive {
 // A notification requests UI validation only; it carries no input authority.
 // Owner IDs are never reused, including across HWND destruction/recreation.
 internal sealed class AcquisitionWakeup:IDisposable {
  static readonly object owners=new object();static int nextOwner;
  readonly object sync=new object();readonly int owner;readonly Func<int,int,bool> post;int request,failures;bool closed;
  public AcquisitionWakeup(Func<int,int,bool> post){this.post=post??throw new ArgumentNullException(nameof(post));lock(owners){owner=nextOwner=checked(nextOwner+1);}}
  public int Failures{get{lock(sync)return failures;}}
  public int Arm(System.Threading.Tasks.Task task){if(task==null)throw new ArgumentNullException(nameof(task));lock(sync){if(closed)throw new ObjectDisposedException(nameof(AcquisitionWakeup));int serial=request=checked(request+1);
   task.ContinueWith(done=>{lock(sync){if(closed||request!=serial)return;try{if(!post(owner,serial)&&failures<int.MaxValue)failures++;}catch{if(failures<int.MaxValue)failures++;}}},System.Threading.CancellationToken.None,System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,System.Threading.Tasks.TaskScheduler.Default);
   return serial;
  }}
  public void Invalidate(){lock(sync){if(!closed)request=checked(request+1);}}
  public bool Matches(long source,long serial){lock(sync)return !closed&&source==owner&&serial==request&&serial>0;}
  public void Dispose(){lock(sync)closed=true;}
 }
 // One trusted desktop collector on its owning UI thread. Its trusted target
 // must enqueue without external I/O; remote focus requires Host confirmation.
 public sealed class WindowsDesktopInput:IDisposable {
  const uint Timer=0x53574431,Confirm=0x8000+317,QueryReady=0x8000+318,ActivateReceiver=0x8000+319,CloseReceiver=0x8000+320;static bool attached;static WindowsDesktopInput draining;static readonly object registration=new object();
  readonly System.Threading.Tasks.TaskCompletionSource<bool> drained=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
  Func<IntPtr,int> removeHook=UnhookError;
  readonly IntPtr window;readonly uint thread;readonly IDesktopInputTarget target;readonly int left,top,width,height;readonly Subclass procedure;readonly MouseProc mouse,keyProcedure;
  AcquisitionWakeup wakeup;long clickSerial,queryClickSerial;
   volatile DesktopHitClient helper,heartbeatHelper;IntPtr hook,keyHook;volatile bool closed,active,session=true,pending,completeClick,awaiting,activating;bool registered,helperFailureReported,heartbeatFailureReported;
   sealed class HeartbeatSample{internal Point Point;internal long Time,Fence;internal ulong Epoch;internal uint Held;internal DesktopHitResult Hit;}
   InputHeartbeat<HeartbeatSample> heartbeat;uint committedHeld;
   // A cursor sample can precede delivery of its low-level MOVE. No heartbeat
   // may renew authority until the ordered native movement stream catches up.
   bool movementPending;long committedMoveOrdinal,mismatchMoveOrdinal,mismatchCommittedMoveOrdinal,mismatchQpc;int mismatchSampleX,mismatchSampleY,mismatchCursorX,mismatchCursorY;bool heartbeatTaskFailureReported;volatile string inputFailure;
   struct DeferredKey { internal Point Point;internal long Time;internal uint Held;internal DesktopHitResult Hit; }
   readonly DeferredKey[] deferredKeys=new DeferredKey[64];int deferredKeyCount;
   Point committedPointerPoint;bool committedPointerValid;ulong committedPointerEpoch;long committedPointerFence;
   bool CommitPointer(Point point,ulong epoch,long token){if(closed||!active||activeEpoch!=epoch||activeFence!=token||fence.Capture()!=token||target.CaptureEpoch!=epoch)return false;committedPointerPoint=point;committedPointerEpoch=epoch;committedPointerFence=token;committedPointerValid=true;return true;}
  readonly InputCommitFence fence=new InputCommitFence();OwnedInputThread owner;uint ownerThread;long clickFence,activeFence;
  internal OwnedInputThread.Diagnostic OwnerDiagnostic=>owner?.ReadDiagnostic();
  sealed class FocusRequest{internal long Id,Fence,Time,Serial;internal ulong Epoch;internal Point Point;internal DesktopHitResult Hit;internal readonly long[] Trace=new long[16];}
  // Last request only; each request owns its array, so late callbacks cannot
  // write into a newer request's diagnostics. Slots 0/1 are identity/time.
  // 2 queued, 3 dispatch, 4-7 validation returns (not their boolean results),
  // 8/9 foreground enter/exit, 10/11 focus enter/exit, 12 owner post,
  // 13 owner callback, 14 expiry. A live snapshot can be partially populated.
  long[] focusTrace;
  static void FocusStamp(FocusRequest request,int stage){System.Threading.Interlocked.Exchange(ref request.Trace[stage],System.Diagnostics.Stopwatch.GetTimestamp());}
  bool TraceForeground(FocusRequest request){FocusStamp(request,8);try{return SetForegroundWindow(window);}finally{FocusStamp(request,9);}}
  void TraceSetFocus(FocusRequest request){FocusStamp(request,10);try{SetFocus(window);}finally{FocusStamp(request,11);}}
  bool TraceDecision(FocusRequest request,int stage,bool result){FocusStamp(request,stage);return result;}
  static long nextFocusRequest;volatile FocusRequest focusRequest;uint routed,suppressed,keyHeld,consumed;long sequence,clickTime;ulong clickEpoch,activeEpoch;Point clickPoint;IntPtr clickList;uint clickPid;
  System.Threading.Tasks.Task<DesktopHitResult> acquisition;Point queryPoint;long queryClickTime;ulong queryClickEpoch;
  long lastHitQuerySequence,lastHitQueryBeganQpc,lastHitQueryEndedQpc,lastHitConsumedQpc,lastHitConsumedTime,lastHitObservedAt,lastHitWindow;uint lastHitKind,lastHitPid;bool lastHitPresent;
  string lastRevocation,lastCandidate,lastPrimaryDown,lastMouseFailure;uint lastNativeMessage,lastMouseMessage,lastPrimaryDownCount;bool lastClickReady;
   readonly long[] mouseTrace=new long[256];long mouseTraceCount,mouseMoveReceived,mouseMoveCommits;
   readonly MouseRawTrace mouseRawTrace;
   internal MouseRawTrace.Snapshot RawMouseDiagnostic=>mouseRawTrace.Read();
  // Diagnostic-only fixed ring. Ordinal is the original mouseTraceCount (one
  // based); slot=(ordinal-1)%64. The legacy four-long offsets stay unchanged.
  internal sealed class MouseRawTrace {
   internal struct Row {public long Ordinal,Qpc,Message,LegacyQpc,LegacyMessage,ForwardQpc,EndQpc;public int Code,MetadataStatus,X,Y;public uint Data,Flags,Time;public ulong Extra;public bool EndRecorded=>EndQpc!=0;}
   // IdentityConsistent only validates event identity; it does not mean the
   // callback ended. ForwardQpc/EndQpc are optional observed stage stamps.
   internal sealed class Snapshot {public int Schema=1,Capacity=64;public long Total,Dropped,Frequency=System.Diagnostics.Stopwatch.Frequency;public bool IdentityConsistent;public Row[] Rows;public int[] UnavailableSlots;}
   readonly long[] legacy,versions=new long[64];readonly Row[] raw=new Row[64];long total;
   public MouseRawTrace(long[] legacy){this.legacy=legacy;}
   public void Begin(long ordinal,long qpc,long message,int code,IntPtr pointer){int slot=(int)((ordinal-1)%64),offset=slot*4;System.Threading.Interlocked.Increment(ref versions[slot]);
    try{legacy[offset]=qpc;legacy[offset+1]=message;legacy[offset+2]=0;legacy[offset+3]=0;
     var row=new Row{Ordinal=ordinal,Qpc=qpc,Message=message,Code=code,MetadataStatus=code!=0?2:pointer==IntPtr.Zero?3:4};
     // Only HC_ACTION provides mouse data. Null/non-action events have an
     // explicit unavailable status. Diagnostic capture never changes routing.
     // Read the native scalar layout without PtrToStructure's extra box.
     // MSLLHOOKSTRUCT: POINT 0/4, DWORDs 8/12/16, ULONG_PTR 24(x64)/20(x86).
     if(code==0&&pointer!=IntPtr.Zero)try{row.X=Marshal.ReadInt32(pointer,0);row.Y=Marshal.ReadInt32(pointer,4);row.Data=unchecked((uint)Marshal.ReadInt32(pointer,8));row.Flags=unchecked((uint)Marshal.ReadInt32(pointer,12));row.Time=unchecked((uint)Marshal.ReadInt32(pointer,16));row.Extra=IntPtr.Size==8?unchecked((ulong)Marshal.ReadInt64(pointer,24)):unchecked((uint)Marshal.ReadInt32(pointer,20));row.MetadataStatus=1;}catch{row.MetadataStatus=4;}
     raw[slot]=row;
    }finally{System.Threading.Interlocked.Increment(ref versions[slot]);System.Threading.Interlocked.Exchange(ref total,ordinal);}
   }
   public void End(long ordinal,long qpc){int slot=(int)((ordinal-1)%64);if(raw[slot].Ordinal!=ordinal)return;System.Threading.Interlocked.Increment(ref versions[slot]);try{legacy[slot*4+3]=qpc;}finally{System.Threading.Interlocked.Increment(ref versions[slot]);}}
   public Snapshot Read(){long count=System.Threading.Interlocked.Read(ref total);var rows=new System.Collections.Generic.List<Row>(64);var unavailable=new System.Collections.Generic.List<int>();
    for(long ordinal=Math.Max(1,count-63);ordinal<=count;ordinal++){int slot=(int)((ordinal-1)%64),offset=slot*4;bool accepted=false;
     // Bounded retries: a busy slot is unavailable, never an excuse to block
     // the input owner or pair old raw fields with a new legacy event.
     for(int retry=0;retry<3;retry++){long before=System.Threading.Interlocked.Read(ref versions[slot]);if((before&1)!=0)continue;var row=raw[slot];row.LegacyQpc=System.Threading.Interlocked.Read(ref legacy[offset]);row.LegacyMessage=System.Threading.Interlocked.Read(ref legacy[offset+1]);row.ForwardQpc=System.Threading.Interlocked.Read(ref legacy[offset+2]);row.EndQpc=System.Threading.Interlocked.Read(ref legacy[offset+3]);long after=System.Threading.Interlocked.Read(ref versions[slot]);
      if(before==after&&(after&1)==0&&row.Ordinal==ordinal&&row.Qpc==row.LegacyQpc&&row.Message==row.LegacyMessage){rows.Add(row);accepted=true;break;}}
     if(!accepted)unavailable.Add(slot);
    }
    return new Snapshot{Total=count,Dropped=Math.Max(0,count-64),IdentityConsistent=unavailable.Count==0,Rows=rows.ToArray(),UnavailableSlots=unavailable.ToArray()};
   }
  }

   long heartbeatSampleTime,heartbeatHitTime,heartbeatTakeQpc,heartbeatValidatedQpc,heartbeatOwnedQpc,heartbeatCommitQpc,heartbeatCommitTime,heartbeatCommits;ulong heartbeatCommitEpoch;string heartbeatRejection;
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Gui{public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretRect;}
  [StructLayout(LayoutKind.Sequential)]struct MouseData{public Point Point;public uint Data,Flags,Time;public UIntPtr Extra;}
  [StructLayout(LayoutKind.Sequential)]struct KeyData{public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
  delegate IntPtr Subclass(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
  delegate IntPtr MouseProc(int code,IntPtr message,IntPtr data);
  delegate bool Visitor(IntPtr hwnd,IntPtr data);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")]static extern uint GetCurrentProcessId();
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")]static extern IntPtr SetFocus(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref Gui info);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")]static extern int GetMessageTime();
  [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,Visitor visit,IntPtr data);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
  [DllImport("user32.dll")]static extern bool GetLayeredWindowAttributes(IntPtr window,out uint key,out byte alpha,out uint flags);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int kind,MouseProc proc,IntPtr module,uint thread);
  [DllImport("user32.dll",SetLastError=true)]static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
  [DllImport("comctl32.dll",SetLastError=true)]static extern bool SetWindowSubclass(IntPtr hwnd,Subclass proc,UIntPtr id,UIntPtr data);
  [DllImport("comctl32.dll")]static extern bool RemoveWindowSubclass(IntPtr hwnd,Subclass proc,UIntPtr id);
  [DllImport("comctl32.dll")]static extern IntPtr DefSubclassProc(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll",SetLastError=true)]static extern UIntPtr SetTimer(IntPtr hwnd,UIntPtr id,uint ms,IntPtr callback);
  [DllImport("user32.dll")]static extern bool KillTimer(IntPtr hwnd,UIntPtr id);
  [DllImport("wtsapi32.dll",SetLastError=true)]static extern bool WTSRegisterSessionNotification(IntPtr hwnd,uint flags);
  [DllImport("wtsapi32.dll")]static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
  [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
  [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
  [DllImport("user32.dll")]static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr value,int index,StringBuilder text,uint length,out uint needed);
  static readonly int[] keyboard={32,37,39,38,40};
  System.Threading.Tasks.Task completion;
  public System.Threading.Tasks.Task Completion{get{lock(drained)return completion??(completion=CompleteSource());}}
  async System.Threading.Tasks.Task ObserveLateCleanup(System.Threading.Tasks.Task ownerDone){try{await ownerDone.ConfigureAwait(false);}catch{}try{await drained.Task.ConfigureAwait(false);}catch{}try{await (helper?.Completion??System.Threading.Tasks.Task.CompletedTask).ConfigureAwait(false);}catch{}try{await (heartbeatHelper?.Completion??System.Threading.Tasks.Task.CompletedTask).ConfigureAwait(false);}catch{}}
  async System.Threading.Tasks.Task CompleteSource(){var ownerDone=owner?.Completion??System.Threading.Tasks.Task.CompletedTask;try{var first=await System.Threading.Tasks.Task.WhenAny(drained.Task,ownerDone).ConfigureAwait(false);await first.ConfigureAwait(false);await System.Threading.Tasks.Task.WhenAll(ownerDone,drained.Task,helper?.Completion??System.Threading.Tasks.Task.CompletedTask,heartbeatHelper?.Completion??System.Threading.Tasks.Task.CompletedTask).ConfigureAwait(false);}catch{_ = ObserveLateCleanup(ownerDone);throw;}}
  internal uint OwnerThreadId=>ownerThread;
   public int HelperProcessId=>helper==null?0:helper.ProcessId;
   public int HeartbeatHelperProcessId=>heartbeatHelper==null?0:heartbeatHelper.ProcessId;
   internal DesktopHitThreadBinding HeartbeatThreadDiagnostic=>heartbeatHelper?.ThreadDiagnostic;
   internal string Failure=>inputFailure??owner?.Failure??(closed?"closed":helper?.Failure??heartbeatHelper?.Failure);
  internal bool Released=>closed&&(owner==null||owner.Completion.IsCompleted)&&consumed==0&&hook==IntPtr.Zero&&keyHook==IntPtr.Zero&&(helper==null||helper.Completion.Status==System.Threading.Tasks.TaskStatus.RanToCompletion)&&(heartbeatHelper==null||heartbeatHelper.Completion.Status==System.Threading.Tasks.TaskStatus.RanToCompletion)&&(heartbeat==null||heartbeat.Completion.IsCompleted);
  // Read on the owning UI thread. A pending remote revoke is not a new-click
  // baseline; Host focus alone also precedes local preheld-key suppression.
  public bool Ready=>!closed&&owner!=null&&owner.Started.Status==System.Threading.Tasks.TaskStatus.RanToCompletion&&helper!=null&&helper.Failure==null&&heartbeatHelper!=null&&heartbeatHelper.Failure==null&&target.GetFocusConfirmation().Ready;
  public bool Focused=>Ready&&active&&target.GetFocusConfirmation().Focused;
  readonly Func<bool> binding;
  public WindowsDesktopInput(IntPtr hwnd,IDesktopInputTarget input,string hitHelper,int x,int y,int w,int h):this(hwnd,input,hitHelper,x,y,w,h,null){}
  public WindowsDesktopInput(IntPtr hwnd,IDesktopInputTarget input,string hitHelper,int x,int y,int w,int h,Func<bool> binding){this.binding=binding;
   mouseRawTrace=new MouseRawTrace(mouseTrace);
   window=hwnd;target=input??throw new ArgumentNullException(nameof(input));thread=GetWindowThreadProcessId(hwnd,out uint pid);left=x;top=y;width=w;height=h;
   if(hwnd==IntPtr.Zero||pid!=GetCurrentProcessId()||thread!=GetCurrentThreadId()||w<=0||h<=0)throw new ArgumentException("desktop-input-owned-ui-thread-and-region");
   if((GetWindowLong(hwnd,-20)&0x800a0)!=0x800a0||!GetLayeredWindowAttributes(hwnd,out uint key,out byte alpha,out uint flags)||(flags&2)==0||alpha!=0)throw new ArgumentException("desktop-input-transparent-receiver");
   lock(registration){if(attached)throw new InvalidOperationException("desktop-input-already-attached");attached=true;}
   procedure=Dispatch;mouse=Mouse;keyProcedure=Keyboard;
   try{if(!DesktopActive())throw new InvalidOperationException("desktop-input-interactive-desktop");wakeup=new AcquisitionWakeup((source,request)=>PostMessage(window,QueryReady,new IntPtr(source),new IntPtr(request)));Check(SetWindowSubclass(hwnd,procedure,(UIntPtr)Timer,UIntPtr.Zero));Check(WTSRegisterSessionNotification(hwnd,0));registered=true;target.Revoke();
    owner=new OwnedInputThread(()=>{ownerThread=GetCurrentThreadId();heartbeat=new InputHeartbeat<HeartbeatSample>();helper=new DesktopHitClient(hitHelper);if(closed)return;heartbeatHelper=new DesktopHitClient(hitHelper);if(closed)return;hook=SetWindowsHookEx(14,mouse,GetModuleHandle(null),0);Check(hook!=IntPtr.Zero);keyHook=SetWindowsHookEx(13,keyProcedure,GetModuleHandle(null),0);Check(keyHook!=IntPtr.Zero);},TickOwner,ReleaseOwner);
   }catch{Dispose();throw;}
  }
  static void Check(bool value){if(!value)throw new Win32Exception(Marshal.GetLastWin32Error());}
  static bool Named(IntPtr value,string expected){var text=new StringBuilder(128);return value!=IntPtr.Zero&&GetUserObjectInformation(value,2,text,256,out uint needed)&&string.Equals(text.ToString(),expected,StringComparison.OrdinalIgnoreCase);}
  bool DesktopActive(){if(!session||!Named(GetProcessWindowStation(),"WinSta0"))return false;IntPtr desktop=OpenInputDesktop(0,false,1);try{return Named(desktop,"Default");}finally{if(desktop!=IntPtr.Zero)CloseDesktop(desktop);}}
  bool Owns(){if(binding!=null&&!binding())return false;var gui=new Gui{Size=(uint)Marshal.SizeOf(typeof(Gui))};if(GetForegroundWindow()!=window||!IsWindowVisible(window)||!IsWindowEnabled(window)||!DesktopActive()||!GetGUIThreadInfo(thread,ref gui)||gui.Active!=window||gui.Focus!=window||(gui.Flags&30)!=0||gui.Capture!=IntPtr.Zero&&gui.Capture!=window)return false;
   bool popup=false;bool enumerated=EnumThreadWindows(thread,(candidate,data)=>{if(candidate!=window&&IsWindowVisible(candidate)&&((uint)GetWindowLong(candidate,-16)&0x80000000u)!=0){popup=true;return false;}return true;},IntPtr.Zero);return enumerated&&!popup;
  }
  bool InRegion(Point point)=>(binding==null||binding())&&point.X>=left&&point.Y>=top&&(long)point.X-left<width&&(long)point.Y-top<height;
  static bool ClassIs(IntPtr hwnd,string expected){var name=new StringBuilder(128);return GetClassName(hwnd,name,name.Capacity)>0&&name.ToString()==expected;}
  bool DesktopCandidate(Point point,out IntPtr list,out uint pid){list=WindowFromPoint(point);GetWindowThreadProcessId(list,out pid);GetWindowThreadProcessId(GetShellWindow(),out uint shell);bool region=InRegion(point),desktop=DesktopActive(),listClass=ClassIs(list,"SysListView32"),parentClass=ClassIs(GetParent(list),"SHELLDLL_DefView");lastCandidate="region="+region+" desktop="+desktop+" pid="+pid+" shell="+shell+" list="+listClass+" parent="+parentClass+" hwnd="+list.ToInt64();return region&&desktop&&pid!=0&&pid==shell&&listClass&&parentClass;}
   DesktopHitResult ValidateHitIdentity(Point point,DesktopHitResult hit){if(!InRegion(point)||!DesktopActive()||hit==null||hit.Classification!=1||WindowFromPoint(point)!=hit.Window)return null;GetWindowThreadProcessId(GetShellWindow(),out uint shell);GetWindowThreadProcessId(hit.Window,out uint pid);return pid==shell&&pid==hit.ShellProcess&&IsWindowVisible(hit.Window)?hit:null;}
   DesktopHitResult ValidateHit(Point point,DesktopHitResult hit)=>hit!=null&&hit.IsFresh(WindowsMachineClock.Milliseconds())?ValidateHitIdentity(point,hit):null;
  int lastBlankX,lastBlankY;string lastBlankRejection;long lastBlankTime;uint lastBlankClassification;
  DesktopHitResult Blank(Point point){lastBlankX=point.X;lastBlankY=point.Y;lastBlankTime=WindowsMachineClock.Milliseconds();lastBlankClassification=0;lastBlankRejection=null;if(!InRegion(point)){lastBlankRejection="outside-region";return null;}if(!DesktopActive()){lastBlankRejection="inactive-desktop";return null;}var hit=helper.Query(point.X,point.Y);lastBlankClassification=hit==null?0:hit.Classification;var valid=ValidateHit(point,hit);if(valid==null)lastBlankRejection=hit==null?"query-null":!hit.IsFresh(WindowsMachineClock.Milliseconds())?"expired":"identity-or-classification";return valid;}
  bool ShellOrReceiver(uint shell){GetWindowThreadProcessId(GetForegroundWindow(),out uint pid);return pid==shell||Owns();}
   void Revoke([System.Runtime.CompilerServices.CallerMemberName]string reason=null){heartbeat?.Invalidate();movementPending=false;committedPointerValid=false;if(deferredKeyCount!=0){Array.Clear(deferredKeys,0,deferredKeyCount);deferredKeyCount=0;}committedHeld=0;if(active||pending||awaiting||activating){lastRevocation=reason;wakeup?.Invalidate();active=pending=completeClick=awaiting=activating=false;focusRequest=null;routed=suppressed=0;fence.Invalidate(target.Revoke,false);}}
  uint Keys(out bool modifier){uint held=routed;if(active)held|=keyHeld;else for(int i=0;i<keyboard.Length;i++)if((GetAsyncKeyState(keyboard[i])&0x8000)!=0)held|=1u<<(i+2);modifier=false;foreach(int key in new[]{16,17,18,91,92})modifier|=(GetAsyncKeyState(key)&0x8000)!=0;if(active){suppressed&=held;held&=~suppressed;}return held;}
  // Capture only while our receiver owns focus. Never retain another app's
  // key events. The raw hook preserves both edges and the original epoch even
  // when down/up arrive together before window messages are dispatched.
  long unknownKeyDowns,unknownKeyUps;
  IntPtr Keyboard(int code,IntPtr message,IntPtr value){try{
   if(code==0&&(closed||!active))heartbeat?.Invalidate();
   if(!closed&&code==0&&active){if(!Owns())Revoke();else{
    var data=(KeyData)Marshal.PtrToStructure(value,typeof(KeyData));ulong epoch=target.CaptureEpoch;long at=EventTime(data.Time);int index=Array.IndexOf(keyboard,(int)data.Key);
    bool modifier=data.Key==16||data.Key==17||data.Key==18||data.Key==91||data.Key==92||data.Key>=160&&data.Key<=165;
    // Unknown non-modifier keys do not change supported input state. They
    // neither refresh evidence nor invalidate an otherwise current heartbeat.
    // Ownership is still checked above and again when that sample is consumed.
    if(!modifier&&index<0){if((data.Flags&0x80)!=0)System.Threading.Interlocked.Increment(ref unknownKeyUps);else System.Threading.Interlocked.Increment(ref unknownKeyDowns);}
    if(modifier||index>=0)heartbeat?.Invalidate();
     if(modifier)Revoke();else if(index>=0){uint bit=1u<<(index+2);if((data.Flags&0x80)!=0)keyHeld&=~bit;else keyHeld|=bit;if(GetCursorPos(out Point point))Publish(point,at,epoch,keyboardEvent:true);else Revoke();}
   }}
  }catch{StopOwner();}return CallNextHookEx(keyHook,code,message,value);}
  long Next(){if(sequence==long.MaxValue)throw new InvalidOperationException("desktop-input-sequence-exhausted");return ++sequence;}
   bool FreshInputEvidence(long at,DesktopHitResult hit){long now=WindowsMachineClock.Milliseconds();return (binding==null||binding())&&at<=now&&now-at<=InputGate.FreshnessMilliseconds&&hit!=null&&hit.IsFresh(now);}
   bool Publish(Point point,long at,ulong epoch,int wheel=0,DesktopHitResult proven=null,Action accepted=null,bool movement=false,bool keyboardEvent=false){long token=activeFence;if(closed||!active||epoch!=activeEpoch||fence.Capture()!=token||!Owns()){Revoke("publish-owner-or-epoch");return false;}var hit=proven??Blank(point);if(hit==null||WindowFromPoint(point)!=hit.Window||!Owns()){Revoke("publish-hit-or-owner");return false;}uint held=Keys(out bool modifier);if(modifier||!Owns()){Revoke("publish-modifier-or-owner");return false;}
    if(movementPending){
      if(!movement){
       if(!keyboardEvent){Revoke("publish-waiting-native-move");return false;}
       if(!FreshInputEvidence(at,hit)||deferredKeyCount==deferredKeys.Length||deferredKeyCount>0&&at<deferredKeys[deferredKeyCount-1].Time){Revoke("publish-invalid-deferred-key");return false;}
       deferredKeys[deferredKeyCount++]=new DeferredKey{Point=point,Time=at,Held=held,Hit=hit};return false;
      }
     if(!FreshInputEvidence(at,hit)){Revoke("publish-stale-native-move");return false;}
     if(!GetCursorPos(out Point current)){Revoke("publish-cursor-unavailable");return false;}
     // Every intermediate MOVE was hit-tested above. A valid intermediate
     // point is not a reason to publish or restart heartbeat sampling.
     if(current.X!=point.X||current.Y!=point.Y)return false;
    }
     if(movementPending&&movement&&deferredKeyCount!=0&&!ValidateDeferredKeys()){Revoke("publish-invalid-deferred-evidence");return false;}
     bool waitingMove=false;bool sent=false;if(!fence.TryCommit(token,()=>{if(closed||!active||target.CaptureEpoch!=epoch||!FreshInputEvidence(at,hit))return;
      if(movementPending){if(!GetCursorPos(out Point current))return;if(current.X!=point.X||current.Y!=point.Y){waitingMove=true;return;}if(!FreshInputEvidence(at,hit))return;if(deferredKeyCount!=0){sent=PublishDeferredKeys(point,at,epoch,hit,token);return;}}
     target.Observe(new InputObservation(Next(),at,true,true,true,true,true,false,false,held,(point.X-left)/(float)width,(point.Y-top)/(float)height,wheel,epoch));if(target.CaptureEpoch==epoch){committedHeld=held;if(movement){movementPending=false;committedMoveOrdinal=mouseTraceCount;if(!CommitPointer(point,epoch,token))return;}accepted?.Invoke();sent=true;}})||!sent){if(waitingMove)return false;Revoke("publish-invalidated");return false;}return true;}

   // Called under the original fence only after all delivered MOVE points have
   // passed their normal checks and the native stream has caught up. Never
   // merge a short key pair or use replay time as new evidence.
   bool ValidateDeferredKeys(){
    for(int i=0;i<deferredKeyCount;i++){var key=deferredKeys[i];if(!FreshInputEvidence(key.Time,key.Hit)||ValidateHitIdentity(key.Point,key.Hit)==null)return false;}
    return Owns();
   }
   bool PublishDeferredKeys(Point point,long at,ulong epoch,DesktopHitResult hit,long token){
    bool moved=false;
    for(int i=0;i<deferredKeyCount;i++){
     var key=deferredKeys[i];
     if(!moved&&at<key.Time){if(!ObserveDeferred(point,at,epoch,committedHeld,hit,token))return false;moved=true;}
     if(!ObserveDeferred(key.Point,key.Time,epoch,key.Held,key.Hit,token))return false;
    }
    if(!moved&&!ObserveDeferred(point,at,epoch,committedHeld,hit,token))return false;
    Array.Clear(deferredKeys,0,deferredKeyCount);deferredKeyCount=0;movementPending=false;committedMoveOrdinal=mouseTraceCount;return CommitPointer(point,epoch,token);
   }
   bool ObserveDeferred(Point point,long at,ulong epoch,uint held,DesktopHitResult hit,long token){
    if(closed||!active||fence.Capture()!=token||target.CaptureEpoch!=epoch||!FreshInputEvidence(at,hit))return false;
    target.Observe(new InputObservation(Next(),at,true,true,true,true,true,false,false,held,(point.X-left)/(float)width,(point.Y-top)/(float)height,0,epoch));
    if(closed||!active||fence.Capture()!=token||target.CaptureEpoch!=epoch)return false;committedHeld=held;return true;
   }
   long EventTime(uint tick){long now=WindowsMachineClock.Milliseconds();return now-unchecked((int)((uint)now-tick));}
   IntPtr Mouse(int code,IntPtr message,IntPtr value){long ordinal=++mouseTraceCount;int slot=(int)((ordinal-1)%64)*4;mouseRawTrace.Begin(ordinal,System.Diagnostics.Stopwatch.GetTimestamp(),message.ToInt64(),code,value);try{return MouseCore(code,message,value,slot);}finally{mouseRawTrace.End(ordinal,System.Diagnostics.Stopwatch.GetTimestamp());}}
   IntPtr MouseCore(int code,IntPtr message,IntPtr value,int traceSlot){bool ownedRelease=false;try{if(code==0){heartbeat?.Invalidate();uint kind=unchecked((uint)message.ToInt64());lastMouseMessage=kind;if(kind==0x200)mouseMoveReceived++;if(kind==0x201){if(lastPrimaryDownCount<uint.MaxValue)lastPrimaryDownCount++;lastPrimaryDown="entered closed="+closed;}
    uint release=kind==0x202?1u:kind==0x205?2u:0;
    ownedRelease=(consumed&release)!=0;if(ownedRelease)consumed&=~release;
    // The release path must not depend on a live transport or author target.
    if(closed){if(ownedRelease){if(consumed==0)FinishDrain();return new IntPtr(1);}return CallNextHookEx(hook,code,message,value);}
    if(release!=0&&!ownedRelease&&!pending)return CallNextHookEx(hook,code,message,value);
    var data=(MouseData)Marshal.PtrToStructure(value,typeof(MouseData));long at=EventTime(data.Time);ulong epoch=target.CaptureEpoch;
    if(active&&(epoch!=activeEpoch||!Owns()))Revoke();
    // OS routing obligations outlive author focus. Never forward an orphan up
    // to Explorer or another app after consuming the corresponding down.
    if(ownedRelease&&!active)return new IntPtr(1);
    bool button=kind==0x201||kind==0x202||kind==0x204||kind==0x205;bool wheel=kind==0x20a;
    // Observe interruption at event time. Sampling only the eventual cursor
    // position would let leaving and returning revive the original click.
    if(pending&&(wheel||kind==0x200&&(Math.Abs((long)data.Point.X-clickPoint.X)>4||Math.Abs((long)data.Point.Y-clickPoint.Y)>4)))Revoke(wheel?"pending-wheel":"pending-pointer-left-click");
    if((awaiting||activating)&&(button||wheel||!Owns()))Revoke();
     // Movement is an event, not permission to reuse an invalidated heartbeat.
     // Validate its original point/time and always let the OS route the move.
     if(active&&kind==0x200){if(Publish(data.Point,at,epoch,movement:true))mouseMoveCommits++;}
    if(active&&(button||wheel)){
     var hit=Blank(data.Point);if(hit==null||!Owns()){Revoke();return ownedRelease?new IntPtr(1):CallNextHookEx(hook,code,message,value);}
     if(kind==0x201)routed|=1;if(kind==0x202)routed&=~1u;if(kind==0x204)routed|=2;if(kind==0x205)routed&=~2u;
     bool accepted=Publish(data.Point,at,epoch,wheel?unchecked((short)(data.Data>>16)):0,hit,()=>{if(kind==0x201)consumed|=1;if(kind==0x204)consumed|=2;});return accepted||ownedRelease?new IntPtr(1):CallNextHookEx(hook,code,message,value);
    }
    // A candidate is not blank evidence. Pass the acquisition click through;
    // its completed pair is classified asynchronously before any focus grant.
    if(!active&&kind==0x201){wakeup?.Invalidate();clickSerial=checked(clickSerial+1);var baseline=target.GetFocusConfirmation();IntPtr candidate=IntPtr.Zero;uint pid=0;lastClickReady=Ready&&baseline.Ready;lastPrimaryDown="x="+data.Point.X+" y="+data.Point.Y+" ready="+lastClickReady+" baseline="+baseline.Ready+" epoch="+baseline.Epoch+" closed="+closed;pending=lastClickReady&&DesktopCandidate(data.Point,out candidate,out pid);completeClick=false;if(pending){clickFence=fence.Capture();clickTime=at;clickEpoch=baseline.Epoch;clickPoint=data.Point;clickList=candidate;clickPid=pid;}}
    // The acquisition click is passed through. Do not ask Explorer for MSAA
    // while it is still handling that click's down/up sequence. Confirm runs
    // after forwarding the up, and performs a fresh full blank query before
    // requesting OS focus; the original down time and epoch stay unchanged.
    else if(!active&&kind==0x202&&pending){GetWindowThreadProcessId(clickList,out uint pid);if(WindowFromPoint(data.Point)!=clickList||pid!=clickPid||Math.Abs((long)data.Point.X-clickPoint.X)>4||Math.Abs((long)data.Point.Y-clickPoint.Y)>4||at-clickTime>InputGate.FreshnessMilliseconds||at<clickTime)Revoke();else{completeClick=true;if(!PostMessage(window,Confirm,IntPtr.Zero,IntPtr.Zero))Revoke();}}
    else if(!active&&button)Revoke();
    }}catch(Exception error){lastMouseFailure=error.GetType().FullName;StopOwner();}System.Threading.Interlocked.Exchange(ref mouseTrace[traceSlot+2],System.Diagnostics.Stopwatch.GetTimestamp());return ownedRelease?new IntPtr(1):CallNextHookEx(hook,code,message,value);}
  // Fixed scalar telemetry: preserve raw reply before validation discards it.
  // These values never grant focus or change click/result freshness.
  void RecordHit(DesktopHitResult hit){lastHitQuerySequence=helper==null?0:helper.LastQuerySequence;lastHitQueryBeganQpc=helper==null?0:helper.LastQueryBeganAt;lastHitQueryEndedQpc=helper==null?0:helper.LastQueryCompletedAt;lastHitConsumedQpc=System.Diagnostics.Stopwatch.GetTimestamp();lastHitConsumedTime=WindowsMachineClock.Milliseconds();lastHitPresent=hit!=null;lastHitKind=hit==null?0:hit.Classification;lastHitPid=hit==null?0:hit.ShellProcess;lastHitWindow=hit==null?0:hit.Window.ToInt64();lastHitObservedAt=hit==null?0:hit.ObservedAt;}
  void Acquire(){if(!pending||!completeClick)return;if(closed||clickFence!=fence.Capture()){Revoke("acquire-ui-invalidated");return;}long now=WindowsMachineClock.Milliseconds();if(now-clickTime>InputGate.FreshnessMilliseconds){owner?.FreezeDiagnostic("acquire-expired");Revoke("acquire-expired-age-"+(now-clickTime));return;}if(target.CaptureEpoch!=clickEpoch){Revoke("acquire-epoch-"+clickEpoch+"-to-"+target.CaptureEpoch);return;}if(!GetCursorPos(out Point point)){Revoke("acquire-cursor-unavailable");return;}if(Math.Abs((long)point.X-clickPoint.X)>4||Math.Abs((long)point.Y-clickPoint.Y)>4){Revoke("acquire-cursor-moved-"+clickPoint.X+","+clickPoint.Y+"-to-"+point.X+","+point.Y);return;}
   // Explorer may need this UI thread to dispatch activation messages while
   // servicing MSAA. Keep the UI pumping during acquisition confirmation.
   // At most one query task is retained; a new click cannot consume its result.
   if(acquisition!=null&&!acquisition.IsCompleted)return;
   if(acquisition!=null&&(queryClickSerial!=clickSerial||queryClickTime!=clickTime||queryClickEpoch!=clickEpoch||queryPoint.X!=point.X||queryPoint.Y!=point.Y))acquisition=null;
   if(!ShellOrReceiver(clickPid))return;
   if(acquisition==null){queryClickSerial=clickSerial;queryPoint=point;queryClickTime=clickTime;queryClickEpoch=clickEpoch;Point requested=point;acquisition=System.Threading.Tasks.Task.Run(()=>helper.QueryForAcquisition(requested.X,requested.Y));wakeup.Arm(acquisition);return;}
   var raw=acquisition.GetAwaiter().GetResult();RecordHit(raw);acquisition=null;
   if(raw==null){Revoke("acquire-query-no-result");return;}
   if(raw.Classification!=1){Revoke("acquire-hit-classification-"+raw.Classification);return;}
   if(!raw.IsFresh(WindowsMachineClock.Milliseconds())){Revoke("acquire-hit-age-"+(WindowsMachineClock.Milliseconds()-raw.ObservedAt));return;}
   if(raw.Window!=clickList||raw.ShellProcess!=clickPid){Revoke("acquire-hit-click-identity");return;}
   var hit=ValidateHit(point,raw);if(hit==null||!hit.IsFresh(WindowsMachineClock.Milliseconds())){Revoke("acquire-hit-current-desktop-or-age");return;}if(!ShellOrReceiver(clickPid))return;
   var request=new FocusRequest{Id=System.Threading.Interlocked.Increment(ref nextFocusRequest),Fence=clickFence,Time=clickTime,Serial=clickSerial,Epoch=clickEpoch,Point=point,Hit=hit};request.Trace[0]=request.Id;request.Trace[1]=request.Time;focusTrace=request.Trace;FocusStamp(request,2);focusRequest=request;pending=false;activating=true;
   if(!PostMessage(window,ActivateReceiver,new IntPtr(request.Id),IntPtr.Zero))Revoke("acquire-focus-post-failed");
  }
  void ConfirmFocus(){if(!awaiting)return;if(closed||clickFence!=fence.Capture()){Revoke("confirm-ui-invalidated");return;}if(WindowsMachineClock.Milliseconds()-clickTime>InputGate.FreshnessMilliseconds||!Owns()||!GetCursorPos(out Point point)||Blank(point)==null){Revoke();return;}var confirmation=target.GetFocusConfirmation();if(!confirmation.Focused&&confirmation.Epoch==clickEpoch)return;if(!confirmation.Focused||clickEpoch==ulong.MaxValue||confirmation.Epoch!=clickEpoch+1){Revoke();return;}uint held=Keys(out bool modifier);if(modifier||!Owns()){Revoke();return;}fence.TryCommit(clickFence,()=>{if(closed)return;suppressed=held;keyHeld=held&~3u;awaiting=false;completeClick=false;active=true;committedHeld=0;activeEpoch=confirmation.Epoch;activeFence=clickFence;if(!CommitPointer(point,confirmation.Epoch,clickFence))Revoke("confirm-pointer-identity");});}
  void PostOwner(Action work){try{owner?.Post(work);}catch(ObjectDisposedException){}catch{if(GetCurrentThreadId()==thread)Dispose();else StopOwner();}}
  void InvalidateUi(){fence.Invalidate(target.Revoke,false);PostOwner(()=>Revoke("receiver-ui-invalidated"));}
  bool CurrentFocusRequest(FocusRequest request){long now=WindowsMachineClock.Milliseconds();return !closed&&request.Fence==fence.Capture()&&request.Time<=now&&now-request.Time<=InputGate.FreshnessMilliseconds&&request.Epoch==target.CaptureEpoch&&request.Time==clickTime&&request.Serial==clickSerial&&request.Epoch==clickEpoch;}
  void FinishFocus(FocusRequest request){FocusStamp(request,13);if(!ReferenceEquals(focusRequest,request)||!activating)return;focusRequest=null;
   if(!CurrentFocusRequest(request)||!request.Hit.IsFresh(WindowsMachineClock.Milliseconds())||ValidateHit(request.Point,request.Hit)==null||!Owns()){Revoke("acquire-native-focus");return;}
   uint held=Keys(out bool modifier);if((GetAsyncKeyState(1)&0x8000)!=0)held|=1;if((GetAsyncKeyState(2)&0x8000)!=0)held|=2;if(modifier||!Owns()){Revoke();return;}
    bool granted=false;if(!fence.TryCommit(request.Fence,()=>{if(closed||!CurrentFocusRequest(request)||!FreshInputEvidence(request.Time,request.Hit))return;target.AcquireFocus(new InputObservation(Next(),clickTime,true,true,true,true,true,false,true,held,(request.Point.X-left)/(float)width,(request.Point.Y-top)/(float)height,0,clickEpoch));activating=false;awaiting=true;granted=true;})||!granted){Revoke("acquire-ui-invalidated");return;}ConfirmFocus();
  }
  // Heartbeats repeat only previously committed state, never sampled raw keys.
   // Mouse, mapped-key and modifier events invalidate the pending sample, even
   // a move away and back. Unknown keys cannot change the committed state.
   void Heartbeat(){
    if(heartbeat.TryTake(out HeartbeatSample sample)){
     heartbeatSampleTime=sample.Time;heartbeatHitTime=sample.Hit==null?0:sample.Hit.ObservedAt;heartbeatTakeQpc=System.Diagnostics.Stopwatch.GetTimestamp();heartbeatValidatedQpc=heartbeatOwnedQpc=heartbeatCommitQpc=heartbeatCommitTime=0;heartbeatCommitEpoch=0;heartbeatRejection=null;
     if(sample.Hit==null||heartbeatHelper.Failure!=null){FailInput("desktop-heartbeat-query-failed");return;}
     if(!active||sample.Epoch!=activeEpoch||sample.Epoch!=target.CaptureEpoch||sample.Fence!=fence.Capture()){Revoke("heartbeat-epoch-or-fence");return;}
     if(!GetCursorPos(out Point current)){Revoke("heartbeat-cursor-unavailable");return;}
      if(current.X!=sample.Point.X||current.Y!=sample.Point.Y){
       mismatchSampleX=sample.Point.X;mismatchSampleY=sample.Point.Y;mismatchCursorX=current.X;mismatchCursorY=current.Y;mismatchMoveOrdinal=mouseTraceCount;mismatchCommittedMoveOrdinal=committedMoveOrdinal;mismatchQpc=System.Diagnostics.Stopwatch.GetTimestamp();
       movementPending=true;heartbeatRejection="waiting-native-move";
       if(!Owns()||!InRegion(current))Revoke("heartbeat-moved-owner-or-region");return;
      }
     bool valid=ValidateHitIdentity(current,sample.Hit)!=null;heartbeatValidatedQpc=System.Diagnostics.Stopwatch.GetTimestamp();if(!valid){Revoke("heartbeat-hit-or-owner");return;}bool owns=Owns();heartbeatOwnedQpc=System.Diagnostics.Stopwatch.GetTimestamp();if(!owns){Revoke("heartbeat-hit-or-owner");return;}
     bool sent=false;
     bool currentFence=fence.TryCommit(sample.Fence,()=>{heartbeatCommitEpoch=target.CaptureEpoch;long now=WindowsMachineClock.Milliseconds();heartbeatCommitTime=now;heartbeatCommitQpc=System.Diagnostics.Stopwatch.GetTimestamp();heartbeatRejection=closed?"closed":!active?"inactive":heartbeatCommitEpoch!=sample.Epoch?"epoch":sample.Time>now||sample.Hit.ObservedAt>now?"future":now-sample.Time>InputGate.FreshnessMilliseconds?"sample-age":!sample.Hit.IsFresh(now)?"hit-age":null;if(heartbeatRejection!=null)return;target.Observe(new InputObservation(Next(),sample.Time,true,true,true,true,true,false,false,sample.Held,(sample.Point.X-left)/(float)width,(sample.Point.Y-top)/(float)height,0,sample.Epoch,true));sent=target.CaptureEpoch==sample.Epoch;if(!sent)heartbeatRejection="post-observe-epoch";});
     // Aging is not renewed authority. Drop this sample without publishing or
     // incrementing sequence; Host's existing 250ms lease keeps counting down.
     // A fresh query may replace it, but never restore an expired focus epoch.
     if(!currentFence||!sent&&heartbeatRejection!="hit-age"&&heartbeatRejection!="sample-age"){Revoke("heartbeat-invalidated");return;}
     if(sent)heartbeatCommits++;
    }
    // Query returns null on protocol/deadline failure. Check even if an event
    // invalidated its sample; obsolescence must not conceal helper failure.
    if(heartbeatHelper.Failure!=null){FailInput("desktop-heartbeat-"+heartbeatHelper.Failure);return;}
    if(movementPending){
      if(closed||!active||activeEpoch!=target.CaptureEpoch||activeFence!=fence.Capture()||!Owns()||!GetCursorPos(out Point waitingPoint)||!InRegion(waitingPoint))Revoke("heartbeat-waiting-move-invalidated");
      return;
     }
     if(!active||heartbeat.Busy)return;
    if(!Owns()||!GetCursorPos(out Point point)||!InRegion(point)){Revoke("heartbeat-owner-or-region");return;}
    // A low-level MOVE can precede the global cursor update. Never sample the
     // old cursor after committing the raw point; wait without renewing authority.
     if(!committedPointerValid||committedPointerEpoch!=activeEpoch||committedPointerFence!=activeFence){Revoke("heartbeat-pointer-identity");return;}
     if(point.X!=committedPointerPoint.X||point.Y!=committedPointerPoint.Y)return;
     var next=new HeartbeatSample{Point=point,Time=WindowsMachineClock.Milliseconds(),Fence=activeFence,Epoch=activeEpoch,Held=committedHeld};
    heartbeat.Begin(()=>{next.Hit=heartbeatHelper.Query(point.X,point.Y);return next;});
   }
   void TickOwner(){if(closed)return;
    if(helper==null||!helper.Running||heartbeatHelper==null||!heartbeatHelper.Running){FailInput("desktop-input-helper-exited:"+(helper?.Failure??heartbeatHelper?.Failure??"process-exit"));return;}
    if(heartbeatHelper!=null&&heartbeatHelper.Failure!=null){FailInput("desktop-heartbeat-"+heartbeatHelper.Failure);return;}
   if((active||pending||awaiting||activating)&&fence.Capture()!=(active?activeFence:clickFence)){Revoke("owner-observed-ui-invalidation");return;}
   if(pending&&completeClick)Acquire();else if(activating){if(WindowsMachineClock.Milliseconds()-clickTime>InputGate.FreshnessMilliseconds){var expired=focusRequest;if(expired!=null)FocusStamp(expired,14);Revoke("native-focus-request-expired");}}
   else if(awaiting)ConfirmFocus();else if(active)Heartbeat();
  }
  IntPtr Dispatch(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data){lastNativeMessage=message;try{
   if(message==0x82||message==CloseReceiver){Dispose();return DefSubclassProc(hwnd,message,w,l);}
   if(message==ActivateReceiver){var request=focusRequest;if(!closed&&request!=null&&request.Id==w.ToInt64()&&request.Fence==fence.Capture()){
      FocusStamp(request,3);if(!TraceDecision(request,4,CurrentFocusRequest(request))||!TraceDecision(request,5,ValidateHit(request.Point,request.Hit)!=null)||!TraceDecision(request,6,ShellOrReceiver(request.Hit.ShellProcess))||!TraceDecision(request,7,CurrentFocusRequest(request))||!request.Hit.IsFresh(WindowsMachineClock.Milliseconds())||!TraceForeground(request)){InvalidateUi();}
     else{TraceSetFocus(request);FocusStamp(request,12);PostOwner(()=>FinishFocus(request));}
    }}
   else if(message==QueryReady){if(!closed&&wakeup!=null&&wakeup.Matches(w.ToInt64(),l.ToInt64()))PostOwner(Acquire);}
   else if(message==Confirm)PostOwner(Acquire);
   else if(message==0x2b1){int change=w.ToInt32();session=change==1||change==3||change==5||change==8;InvalidateUi();}
   else if(message==8){GetWindowThreadProcessId(w,out uint nextPid);if(active||awaiting||activating||pending&&nextPid!=0&&nextPid!=clickPid)InvalidateUi();}
   else if(message==0x1f||message==0x211||message==0x18&&w==IntPtr.Zero)InvalidateUi();
  }catch{Dispose();}return DefSubclassProc(hwnd,message,w,l);}

  static int UnhookError(IntPtr value){if(UnhookWindowsHookEx(value))return 0;int error=Marshal.GetLastWin32Error();return error==0?31:error;}
  int RemoveHook(ref IntPtr value){if(value==IntPtr.Zero)return 0;int error=(removeHook??UnhookError)(value);if(error==0||error==1404){value=IntPtr.Zero;return 0;}return error;}
  void FinishDrain(){
   // Repeated disposal of an already-drained source must not clear a newer
   // collector's registration. A failed removal retains its handle and root.
   if(consumed==0&&hook==IntPtr.Zero&&keyHook==IntPtr.Zero&&drained.Task.IsCompleted)return;
   lock(registration){draining=this;attached=true;}
   int keyError=RemoveHook(ref keyHook),mouseError=consumed==0?RemoveHook(ref hook):0;
   if(keyError!=0||mouseError!=0)drained.TrySetException(new Win32Exception(keyError!=0?keyError:mouseError));
   if(consumed!=0||hook!=IntPtr.Zero||keyHook!=IntPtr.Zero)return;
   lock(registration){draining=null;attached=false;}drained.TrySetResult(true);
  }
   void FailInput(string reason){owner?.FreezeDiagnostic("input-failure");inputFailure=reason;StopOwner();}
   void StopOwner(){closed=true;wakeup?.Dispose();try{fence.Invalidate(target.Revoke,true);}catch{}owner?.Dispose();PostMessage(window,CloseReceiver,IntPtr.Zero,IntPtr.Zero);}
   bool ReleaseOwner(){closed=true;active=pending=completeClick=awaiting=activating=false;movementPending=false;committedPointerValid=false;if(deferredKeyCount!=0){Array.Clear(deferredKeys,0,deferredKeyCount);deferredKeyCount=0;}focusRequest=null;routed=keyHeld=suppressed=committedHeld=0;heartbeat?.Close();helper?.Dispose();heartbeatHelper?.Dispose();FinishDrain();if(helper!=null&&helper.Completion.IsCompleted&&!helperFailureReported){helperFailureReported=true;helper.Completion.GetAwaiter().GetResult();}if(heartbeatHelper!=null&&heartbeatHelper.Completion.IsCompleted&&!heartbeatFailureReported){heartbeatFailureReported=true;heartbeatHelper.Completion.GetAwaiter().GetResult();}if(heartbeat!=null&&heartbeat.Completion.IsCompleted&&!heartbeatTaskFailureReported){heartbeatTaskFailureReported=true;heartbeat.Completion.GetAwaiter().GetResult();}return consumed==0&&hook==IntPtr.Zero&&keyHook==IntPtr.Zero&&(helper==null||helper.Completion.IsCompleted)&&(heartbeatHelper==null||heartbeatHelper.Completion.IsCompleted)&&(heartbeat==null||heartbeat.Completion.IsCompleted);}
  public void Dispose(){if(GetCurrentThreadId()!=thread)throw new InvalidOperationException("desktop-input-dispose-ui-thread");closed=true;wakeup?.Dispose();try{fence.Invalidate(target.Revoke,true);}catch{}finally{
   RemoveWindowSubclass(window,procedure,(UIntPtr)Timer);if(registered){WTSUnRegisterSessionNotification(window);registered=false;}
   if(owner!=null)owner.Dispose();else{helper?.Dispose();heartbeatHelper?.Dispose();FinishDrain();}
  }}

 }
}

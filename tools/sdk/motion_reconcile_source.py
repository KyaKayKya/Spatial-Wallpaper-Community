"""Deterministic native substitutions for controlled input replay; never desktop evidence."""
def body(text,signature,replacement):
 start=text.index(signature);opening=text.index('{',start);depth=1;end=opening+1
 while depth:depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[:opening]+replacement+text[end:]

def transformed_source(original,negative=None):
 text=original
 if negative=='requery':
  old='if(movementPending){\n      if(closed';assert text.count(old)==1;text=text.replace(old,'if(false){\n      if(closed')
 if negative=='skip-intermediate':
  old='var hit=proven??Blank(point);';assert text.count(old)==1;text=text.replace(old,'if(movementPending&&movement&&point.X!=acceptanceCursor.X)return false;'+old)
 if negative=='release-forward':
  old='return accepted||ownedRelease?new IntPtr(1):CallNextHookEx(hook,code,message,value);';assert text.count(old)==1;text=text.replace(old,'return accepted?new IntPtr(1):CallNextHookEx(hook,code,message,value);')
 if negative=='deferred-time':
  old='bool ObserveDeferred(Point point,long at,ulong epoch,uint held,DesktopHitResult hit,long token){';assert text.count(old)==1;text=text.replace(old,old+'at=WindowsMachineClock.Milliseconds();')
 if negative=='deferred-held':
  old='ObserveDeferred(point,at,epoch,committedHeld,hit,token)';assert text.count(old)==2;text=text.replace(old,'ObserveDeferred(point,at,epoch,keyHeld,hit,token)')
 if negative=='deferred-identity':
  old='||ValidateHitIdentity(key.Point,key.Hit)==null';assert text.count(old)==1;text=text.replace(old,'')
 if negative=='cursor-lag':
  old='if(point.X!=committedPointerPoint.X||point.Y!=committedPointerPoint.Y)return;';assert text.count(old)==1;text=text.replace(old,'')
 text=body(text,'bool Owns()','{return acceptanceOwns;}')
 text=text.replace('[DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);','static short GetAsyncKeyState(int key)=>0;')
 text=text.replace('[DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);','static bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l)=>true;')
 text=body(text,'DesktopHitResult Blank(', '{acceptanceBlanks++;return InRegion(point)&&point.X!=99?new DesktopHitResult(1,1,1,new IntPtr(1),AcceptanceNow-acceptanceHitAge):null;}')
 text=body(text,'DesktopHitResult ValidateHitIdentity(', '{return InRegion(point)&&point.X!=99&&point.X!=acceptanceBlockedX&&hit!=null&&hit.Classification==1?hit:null;}')
 for old,new in [('[DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);','bool GetCursorPos(out Point point){point=acceptanceCursor;return true;}'),('[DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);','static IntPtr WindowFromPoint(Point point)=>new IntPtr(1);'),('[DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);','IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data){acceptanceForwards++;return IntPtr.Zero;}')]:
  assert text.count(old)==1; text=text.replace(old,new)
 text=text.replace('WindowsMachineClock.Milliseconds()','AcceptanceNow').replace('heartbeatHelper.Failure','acceptanceFailure').replace('heartbeatHelper.Query(point.X,point.Y)','AcceptanceQuery(point.X,point.Y)')
 text=text.replace('const uint Timer=', 'static long AcceptanceNow;long acceptanceHitAge;bool acceptanceOwns;Point acceptanceCursor;int acceptanceBlockedX,acceptanceBlanks,acceptanceQueries,acceptanceForwards;string acceptanceFailure;DesktopHitResult AcceptanceQuery(int x,int y){System.Threading.Interlocked.Increment(ref acceptanceQueries);return new DesktopHitResult(1,1,1,new IntPtr(1),AcceptanceNow);}const uint Timer=',1)
 return text

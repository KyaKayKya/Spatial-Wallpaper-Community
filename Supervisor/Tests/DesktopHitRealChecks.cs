using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Windows.Forms;using SpatialWallpaper.Supervision.Interactive;
static class DesktopHitRealChecks {
 delegate bool EnumProc(IntPtr window,IntPtr state);
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr state);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string name);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 static void Need(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Pump(int ms){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(1);}}
 [STAThread]static int Main(string[] args){dynamic shell=null;IntPtr foreground=GetForegroundWindow();DesktopHitClient client=null;Process child=null;try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));shell.MinimizeAll();Pump(400);
  uint shellPid;GetWindowThreadProcessId(GetShellWindow(),out shellPid);IntPtr list=IntPtr.Zero;EnumWindows((window,state)=>{var view=FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null);var candidate=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);uint pid;GetWindowThreadProcessId(candidate,out pid);if(view!=IntPtr.Zero&&candidate!=IntPtr.Zero&&pid==shellPid)list=candidate;return true;},IntPtr.Zero);
  Need(list!=IntPtr.Zero&&GetWindowRect(list,out Rect unused),"actual-explorer-desktop-present");Rect area;GetWindowRect(list,out area);
  client=new DesktopHitClient(args[0]);child=Process.GetProcessById(client.ProcessId);var retained=child.Handle;bool blank=false,icon=false;int bx=0,by=0,queries=0;double maximum=0;
  for(int y=area.Top+20;y<area.Bottom&&!(blank&&icon);y+=40)for(int x=area.Left+20;x<area.Right&&!(blank&&icon);x+=40){var clock=Stopwatch.StartNew();var hit=client.Query(x,y);maximum=Math.Max(maximum,clock.Elapsed.TotalMilliseconds);queries++;Need(hit!=null,"real-query-reply-"+queries+"-"+client.Failure);Need(hit.ShellProcess==shellPid&&hit.Window==list&&hit.ShellCreated>0,"real-shell-identity");if(hit.Classification==1){blank=true;bx=x;by=y;}if(hit.Classification==2)icon=true;}
  Need(blank,"actual-visible-blank-classified");Need(icon,"actual-icon-excluded");
  using(var cover=new Form()){cover.StartPosition=FormStartPosition.Manual;cover.Bounds=new System.Drawing.Rectangle(bx-10,by-10,160,100);cover.TopMost=true;cover.Show();Pump(100);var hit=client.Query(bx+30,by+30);Need(hit!=null&&hit.Classification==3,"owned-window-cover-excluded");cover.Hide();Pump(50);}
  for(int i=0;i<100;i++){var clock=Stopwatch.StartNew();var hit=client.Query(bx,by);maximum=Math.Max(maximum,clock.Elapsed.TotalMilliseconds);Need(hit!=null&&hit.Classification==1,"stable-visible-blank-"+i);}
  Console.WriteLine("METRIC queries="+(queries+101)+" maximumMs="+maximum.ToString("F3"));return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{if(client!=null){client.Dispose();if(!client.Completion.Wait(3000))throw new Exception("helper-cleanup-timeout");}if(child!=null){Need(child.WaitForExit(1000),"real-helper-kernel-exited");child.Dispose();}if(shell!=null){shell.UndoMinimizeALL();Marshal.FinalReleaseComObject(shell);}if(foreground!=IntPtr.Zero)SetForegroundWindow(foreground);}}
}

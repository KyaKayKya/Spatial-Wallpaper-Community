using System;using System.Drawing;using System.Reflection;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class PreviewInputChecks {
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
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 static Form window,other;static InputTarget target;static InputFrames frames;static uint injected;
 sealed class FailingTarget:IPreviewInputTarget {
  internal bool Failed;public ulong CaptureEpoch=>target.CaptureEpoch;
  public void Observe(InputObservation value){if(Failed)throw new System.IO.IOException("fixture-input-transport-lost");target.Observe(value);}
  public void Revoke(){if(Failed)throw new System.IO.IOException("fixture-input-transport-lost");target.Revoke();}
 }
 sealed class FixtureForm:Form {protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0x201||message.Msg==0x202||message.Msg==0x215||message.Msg==8)Console.WriteLine("native message "+message.Msg.ToString("x")+" focus="+Focused+" capture="+Capture+" new="+message.LParam);}}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Pump(int ms){var until=Environment.TickCount+ms;do{Application.DoEvents();Thread.Sleep(1);}while(unchecked(Environment.TickCount-until)<0);}
 static void Advance(){frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);}
 static void Focus(Form form,bool bootstrap=false){form.Show();form.Activate();SetForegroundWindow(form.Handle);Pump(40);var p=form.PointToScreen(new PointFAdapter(form.ClientSize.Width/2,form.ClientSize.Height/2).Point);if(bootstrap&&GetForegroundWindow()!=form.Handle){var point=new Point{X=p.X,Y=p.Y};Check(WindowFromPoint(point)==form.Handle,"bootstrap-click-only-owned-window");SetCursorPos(p.X,p.Y);Send(new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}});Pump(40);}Check(GetForegroundWindow()==form.Handle,"fixture owns foreground");SetCursorPos(p.X,p.Y);Pump(30);}
 struct PointFAdapter {public System.Drawing.Point Point;public PointFAdapter(int x,int y){Point=new System.Drawing.Point(x,y);}}
 static void Send(params Event[] values){Check(SendInput((uint)values.Length,values,Marshal.SizeOf(typeof(Event)))==values.Length,"native injection delivered");}
 static void Key(int key,bool down){Check(GetForegroundWindow()==window.Handle||GetForegroundWindow()==other.Handle,"key injection owned focus");Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=(ushort)key,Flags=down?0u:2u}}});if(key==32)injected=down?injected|1:injected&~1u;if(key==17)injected=down?injected|2:injected&~2u;Pump(25);}
 static void Click(){Check(GetForegroundWindow()==window.Handle,"click owned focus");Send(new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}});Pump(30);Advance();}
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--external")return ExternalWindow();
  IntPtr previous=GetForegroundWindow();Point cursor;GetCursorPos(out cursor);IDisposable source=null;
  try{
   var type=typeof(InputBroker).Assembly.GetType("SpatialWallpaper.Sdk.Editor.Native.WindowsPreviewInput");Check(type!=null,"native-preview-input-available");
   foreach(int key in new[]{1,2,16,17,18,32,37,38,39,40,91,92})if((GetAsyncKeyState(key)&0x8000)!=0)throw new InvalidOperationException("fixture cannot run while user input is held");
   using(var broker=new InputBroker())using(window=new FixtureForm{Text="SDK native preview input acceptance",ClientSize=new Size(320,180),StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(90,90)})using(other=new Form{Text="SDK excluded application fixture",ClientSize=new Size(280,160),StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(470,90)}){
    target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false);frames=new InputFrames();
    window.Show();other.Show();Focus(window,true);
    if(args.Length==0)source=(IDisposable)Activator.CreateInstance(type,new object[]{window.Handle,target});
    if(source!=null){bool duplicate=false;try{Activator.CreateInstance(type,new object[]{window.Handle,target});}catch(TargetInvocationException e){duplicate=e.InnerException is InvalidOperationException;}Check(duplicate,"duplicate native attachment rejected");}
    Advance();Check(!frames.Focused,"hover and OS focus alone do not acquire");
    Key(32,true);Advance();Check(!frames.Focused&&!frames.GetButton("Space"),"unfocused key not delivered");
    Click();Console.WriteLine("click snapshot focused="+frames.Focused+" down="+frames.GetButtonDown("Primary")+" up="+frames.GetButtonUp("Primary"));Check(frames.Focused&&frames.GetButtonDown("Primary")&&frames.GetButtonUp("Primary"),"real primary click reaches shared frame");
    Check(!frames.GetButton("Space"),"preheld Space suppressed on acquisition");
    Check(Math.Abs(frames.X-.5f)<.02&&Math.Abs(frames.Y-.5f)<.02,"client coordinates normalized");
    Key(32,false);Advance();Check(!frames.GetButtonUp("Space"),"preheld release is not author edge");
    Key(32,true);Advance();Check(frames.GetButton("Space")&&frames.GetButtonDown("Space"),"native Space down delivered");
    Advance();Check(frames.GetButton("Space")&&!frames.GetButtonDown("Space"),"held state does not repeat edge");
    Key(32,false);Advance();Check(!frames.GetButton("Space")&&frames.GetButtonUp("Space"),"native Space up delivered");
    foreach(var pair in new[]{Tuple.Create(37,"Left"),Tuple.Create(39,"Right"),Tuple.Create(38,"Up"),Tuple.Create(40,"Down")}){Key(pair.Item1,true);Advance();Check(frames.GetButton(pair.Item2)&&frames.GetButtonDown(pair.Item2),"native "+pair.Item2+" down");Key(pair.Item1,false);Advance();Check(frames.GetButtonUp(pair.Item2),"native "+pair.Item2+" up");}
    Send(new Event{Data=new Payload{Mouse=new Mouse{Flags=0x800,Data=120}}});Pump(30);Advance();Check(frames.Wheel==1,"native wheel normalized");
    Focus(other);Advance();Check(!frames.Focused&&!frames.GetButton("Space")&&frames.X==0&&frames.Y==0&&frames.Wheel==0,"other window revokes all state");
    Key(32,true);Key(32,false);Advance();Check(!frames.Focused&&!frames.GetButtonDown("Space"),"other window keys never delivered");
    Focus(window);Advance();Check(!frames.Focused,"returning OS focus does not reacquire");Click();Check(frames.Focused,"fresh click reacquires");
    Key(17,true);Advance();Check(!frames.Focused,"modifier revokes");Key(17,false);Advance();Check(!frames.Focused,"modifier release does not reacquire");Click();Check(frames.Focused,"click after modifier reacquires");
    using(var menu=new ContextMenuStrip()){menu.Items.Add("excluded menu item");menu.Show(window,new System.Drawing.Point(100,60));Pump(50);Advance();Check(!frames.Focused,"open menu revokes");menu.Close();}Focus(window);Click();Check(frames.Focused,"menu close still needs fresh click");
    Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32}}});injected|=1;Thread.Sleep(350);Pump(30);Advance();Check(!frames.Focused&&!frames.GetButtonDown("Space"),"stale queued key rejected");Key(32,false);Click();Check(frames.Focused,"fresh click after stale queue reacquires");
    ExternalCheck();Focus(window);Advance();Check(!frames.Focused,"external process exit cannot reacquire");Click();
    window.Hide();Pump(40);Advance();Check(!frames.Focused,"hidden target revokes");Focus(window);Click();Check(frames.Focused,"visible target needs new click");
    source.Dispose();source=null;Advance();Check(!frames.Focused,"adapter disposal revokes");Click();Check(!frames.Focused,"disposed adapter cannot revive");
    var failing=new FailingTarget();source=(IDisposable)Activator.CreateInstance(type,new object[]{window.Handle,failing});failing.Failed=true;source.Dispose();source=null;
    source=(IDisposable)Activator.CreateInstance(type,new object[]{window.Handle,target});source.Dispose();source=null;Check(true,"transport failure still detaches native callback and registration");
    target.Dispose();Console.WriteLine("PASS native preview input acceptance; not Explorer desktop acceptance");
   }return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{source?.Dispose();if(injected!=0){var list=new System.Collections.Generic.List<Event>();if((injected&1)!=0)list.Add(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32,Flags=2}}});if((injected&2)!=0)list.Add(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=17,Flags=2}}});SendInput((uint)list.Count,list.ToArray(),Marshal.SizeOf(typeof(Event)));}SetCursorPos(cursor.X,cursor.Y);if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}
 }
 static int ExternalWindow(){using(var form=new Form{Text="SDK separate process exclusion fixture",ClientSize=new Size(280,160),StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(470,300),KeyPreview=true}){int down=0,up=0;form.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Space)down++;};form.KeyUp+=(s,e)=>{if(e.KeyCode==Keys.Space)up++;};form.Show();form.Activate();SetForegroundWindow(form.Handle);Pump(50);Console.WriteLine("external-ready:"+form.Handle.ToInt64());var end=Environment.TickCount+5000;while(up==0&&unchecked(Environment.TickCount-end)<0)Pump(5);Console.WriteLine("external-keys:"+down+":"+up);return down==1&&up==1?0:1;}}
 static void ExternalCheck(){
  using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--external"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
   try{var ready=child.StandardOutput.ReadLineAsync();int end=Environment.TickCount+5000;while(!ready.IsCompleted&&unchecked(Environment.TickCount-end)<0)Pump(5);Check(ready.IsCompleted&&ready.Result.StartsWith("external-ready:"),"external fixture ready");var handle=new IntPtr(long.Parse(ready.Result.Substring(15)));Check(GetForegroundWindow()==handle,"external fixture owns foreground");Advance();Check(!frames.Focused,"separate process focus revokes");Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32}}},new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32,Flags=2}}});while(!child.HasExited&&unchecked(Environment.TickCount-end)<0)Pump(5);Check(child.HasExited&&child.ExitCode==0,"external process received its own key pair");Advance();Check(!frames.Focused&&!frames.GetButtonDown("Space"),"external process keys excluded");}
   finally{if(!child.HasExited)child.Kill();child.WaitForExit();}
  }
 }
}

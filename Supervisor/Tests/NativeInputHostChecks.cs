using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using System.Diagnostics;using System.Drawing;using System.Windows.Forms;using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Editor.Native;
static class NativeInputHostChecks {
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
 static JObject descriptor;static bool space;static Form window;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 static void Pump(int ms){var clock=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(clock.ElapsedMilliseconds<ms);}
 static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){var task=stream.ReadAsync(bytes,at,bytes.Length-at);if(!task.Wait(3000))throw new TimeoutException("manager-read");int n=task.Result;if(n==0)throw new EndOfStreamException();at+=n;}}
 static NamedPipeClientStream Open(string operation,JObject payload){var pipe=new NamedPipeClientStream(".",(string)descriptor["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous);try{pipe.Connect(3000);byte[] body=Encoding.UTF8.GetBytes(new JObject{{"auth",descriptor["auth"]},{"operation",operation},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None));pipe.Write(BitConverter.GetBytes(body.Length),0,4);pipe.Write(body,0,body.Length);return pipe;}catch{pipe.Dispose();throw;}}
 static JObject Call(string operation,JObject payload){using(var pipe=Open(operation,payload)){var header=new byte[4];Exact(pipe,header);int count=BitConverter.ToInt32(header,0);Need(count>0&&count<=32768,"reply-bound");var result=new byte[count];Exact(pipe,result);return JObject.Parse(Encoding.UTF8.GetString(result));}}
 static void Send(params Event[] values){Need(GetForegroundWindow()==window.Handle,"owned-foreground-before-input");Need(SendInput((uint)values.Length,values,Marshal.SizeOf(typeof(Event)))==values.Length,"native-input-delivered");}
 static long Revision(string path){try{using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(file)){reader.ReadUInt64();return reader.ReadInt64();}}catch(FileNotFoundException){return 0;}catch(DirectoryNotFoundException){return 0;}}
 static void AwaitRevision(string path,long expected){var clock=Stopwatch.StartNew();while(Revision(path)<expected&&clock.ElapsedMilliseconds<5000)Pump(5);Need(Revision(path)==expected,"author-storage-revision-"+expected);}
 [STAThread]static int Main(string[] args){IntPtr previous=GetForegroundWindow();GetCursorPos(out var cursor);try{
  foreach(int key in new[]{1,2,16,17,18,32,37,38,39,40,91,92})Need((GetAsyncKeyState(key)&0x8000)==0,"no-preexisting-held-input-"+key);
  string output=args[0],state=Path.Combine(output,"state"),build=args[2];descriptor=JObject.Parse(File.ReadAllText(Path.Combine(state,"endpoint.json")));Guid installation=Guid.NewGuid();string instance=Guid.NewGuid().ToString("N");var measurements=new JObject();
  Need((bool?)Call("StartPreview",new JObject{{"installation",installation.ToString("N")},{"build",build},{"instance",instance},{"generation",1},{"intent","Manual"},{"package",args[1]}})["ok"]==true,"host-preview-start");
  var clock=Stopwatch.StartNew();JToken status=null;while(clock.ElapsedMilliseconds<20000){status=Call("Status",new JObject())["instances"]?[instance];if((string)status?["state"]=="Running")break;Thread.Sleep(100);}
  Need((string)status?["state"]=="Running","independent-preview-worker-running");
  foreach(string wrong in new[]{"instance","attempt","generation","authentication"}){
   var payload=new JObject{{"instance",instance},{"attempt",status["attempt"]},{"generation",1}};JToken auth=descriptor["auth"].DeepClone();
   if(wrong=="generation")payload[wrong]=2;else if(wrong=="authentication")descriptor["auth"]=Convert.ToBase64String(new byte[32]);else payload[wrong]=Guid.NewGuid().ToString("N");
   bool rejected=false;try{using(var invalid=new PreviewInputChannel(Open("OpenPreviewInput",payload))){} }catch(InvalidDataException){rejected=true;}finally{descriptor["auth"]=auth;}
   Need(rejected,"Host-rejects-wrong-input-"+wrong);
  }
  string snapshot=Path.Combine(state,"storage","preview-"+installation.ToString("N"),"state.bin");
  using(var channel=new PreviewInputChannel(Open("OpenPreviewInput",new JObject{{"instance",instance},{"attempt",status["attempt"]},{"generation",1}})))
  using(window=new Form{Text="SDK native input / independent Host / LPAC author",ClientSize=new Size(400,240),StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(100,120)}){
   window.Show();window.Activate();SetForegroundWindow(window.Handle);Pump(50);var p=window.PointToScreen(new System.Drawing.Point(100,180));SetCursorPos(p.X,p.Y);
   using(var source=args.Length>4&&args[4]=="--no-adapter"?null:new WindowsPreviewInput(window.Handle,channel)){
    Pump(100);Need(Revision(snapshot)==0,"no-author-input-before-real-click");
    Send(new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}});Pump(50);
    Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32}}},new Event{Data=new Payload{Mouse=new Mouse{Flags=0x800,Data=120}}});space=true;
    AwaitRevision(snapshot,1);
    Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32,Flags=2}}});space=false;AwaitRevision(snapshot,2);
    int soak=args.Length>5?int.Parse(args[5]):0;if(soak>0){
     using(var self=Process.GetCurrentProcess())using(var backend=Process.GetProcessById(int.Parse(args[3]))){
      self.Refresh();backend.Refresh();double uiCpu=self.TotalProcessorTime.TotalMilliseconds,hostCpu=backend.TotalProcessorTime.TotalMilliseconds;long uiMemory=self.PrivateMemorySize64,hostMemory=backend.PrivateMemorySize64;
      var run=Stopwatch.StartNew();long last=0,maxGap=0,ticks=0;
      while(run.ElapsedMilliseconds<soak*1000L){Pump(1);long at=run.ElapsedMilliseconds;maxGap=Math.Max(maxGap,at-last);last=at;ticks++;if(GetForegroundWindow()!=window.Handle)throw new Exception("soak-foreground-lost");}
      self.Refresh();backend.Refresh();measurements=new JObject{{"seconds",soak},{"ui_pump_iterations",ticks},{"max_ui_pump_gap_ms",maxGap},{"ui_cpu_ms",self.TotalProcessorTime.TotalMilliseconds-uiCpu},{"host_cpu_ms",backend.TotalProcessorTime.TotalMilliseconds-hostCpu},{"ui_private_delta_bytes",self.PrivateMemorySize64-uiMemory},{"host_private_delta_bytes",backend.PrivateMemorySize64-hostMemory}};
      File.WriteAllText(Path.Combine(output,"soak.json"),measurements.ToString());Need(Revision(snapshot)==2,"soak-keeps-focus-without-false-clear");Need(maxGap<200,"soak-ui-pump-within-input-channel-deadline");
     }
    }
    window.Hide();AwaitRevision(snapshot,3);
   }
  }
  Need((bool?)Call("Stop",new JObject{{"instance",instance}})["ok"]==true,"Host-input-stop");Need((bool?)Call("Exit",new JObject())["ok"]==true,"Host-input-exit");using(var host=Process.GetProcessById(int.Parse(args[3])))Need(host.WaitForExit(5000),"Host-input-process-exit");
  using(var journal=new FaultJournal(Path.Combine(output,"observer-journal")))using(var store=new PrivateStore(Path.Combine(state,"storage"))){var session=new BrokerSession(journal.Begin(installation,build,Guid.NewGuid(),2,StartIntent.Manual),true,new[]{"private-storage"});var saved=store.Read(session);Need(saved.Version==3,"durable-preview-input-revision");foreach(string key in new[]{"input-held","input-released","input-cleared"})Need(Encoding.UTF8.GetString(saved.Get(key))=="observed","durable-author-"+key);}
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"independent_host",true},{"native_input",true},{"author_durable_revision",3},{"stop_exit",true},{"soak",measurements}}.ToString());return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 finally{if(space)SendInput(1,new[]{new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32,Flags=2}}}},Marshal.SizeOf(typeof(Event)));SetCursorPos(cursor.X,cursor.Y);if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}}
}

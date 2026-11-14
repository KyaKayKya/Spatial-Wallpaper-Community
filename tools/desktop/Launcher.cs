using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

// Windows GUI entry point: no command interpreter, console, or camera ownership.
static class Launcher {
 [STAThread]static int Main(string[] args){
  string directory=AppDomain.CurrentDomain.BaseDirectory;
  try{
   var arguments=new List<string>(args);
   int rootIndex=arguments.IndexOf("--desktop-data-root");
   if(arguments.Contains("--desktop-tray-open")&&rootIndex>=0&&rootIndex+1<arguments.Count&&File.Exists(Path.Combine(arguments[rootIndex+1],"shutdown.requested")))return 0;
   if(!arguments.Contains("--tracking-python")){
    string python=ConfigurationManager.AppSettings["TrackingPython"];
    if(string.IsNullOrEmpty(python))python=Path.Combine("Python","python.exe");
    if(!Path.IsPathRooted(python))python=Path.Combine(directory,python);
    if(string.IsNullOrEmpty(python)||!File.Exists(python))throw new FileNotFoundException("Tracker Python is not configured",python);
    arguments.Add("--tracking-python");arguments.Add(python);
   }
   if(!arguments.Contains("--tracking-root")){arguments.Add("--tracking-root");arguments.Add(Path.Combine(directory,"Tracker"));}
   // Delay showing Runtime without assigning an external swap-chain target.
   // Its own Unity HWND remains both the rendering and desktop window.
   if(arguments.Contains("--desktop-runtime")){arguments.Add("-parentHWND");arguments.Add("delayed");}
    var command=new StringBuilder();foreach(string argument in arguments){if(command.Length>0)command.Append(' ');command.Append(Quote(argument));}
    using(var player=Process.Start(new ProcessStartInfo(Path.Combine(directory,"SpatialWallpaper.exe"),command.ToString()){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true})){
     player.WaitForExit();return player.ExitCode;
    }
  }catch(Exception error){
   File.WriteAllText(Path.Combine(directory,"launcher-error.log"),error.ToString());
   MessageBox.Show("无法启动 Spatial Wallpaper。详情已记录到 launcher-error.log。","Spatial Wallpaper",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
  }
 }
 static string Quote(string value){
  var text=new StringBuilder("\"");int slashes=0;
  foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){text.Append('\\',slashes*2+1);text.Append(c);}else{text.Append('\\',slashes);text.Append(c);}slashes=0;}
  text.Append('\\',slashes*2);text.Append('"');return text.ToString();
 }
}

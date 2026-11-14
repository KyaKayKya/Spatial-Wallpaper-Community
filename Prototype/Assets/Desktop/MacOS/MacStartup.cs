using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
namespace SpatialWallpaper.Desktop {
 // A per-user LaunchAgent runs on the next login. Toggling never starts/stops
 // the existing Runtime and never needs an administrator or global daemon.
 public static class MacStartup {
  const string Label="com.spatialwallpaper.manager";
  static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),"Library","LaunchAgents",Label+".plist");
  static string[] Arguments(){
   string executable;using(var own=Process.GetCurrentProcess())executable=own.MainModule.FileName;
   var args=new System.Collections.Generic.List<string>{executable,"--desktop-enabled","true","--desktop-data-root",DesktopRuntime.DataRoot};
   foreach(string option in new[]{"--tracking-python","--tracking-root","--data-root"}){string value=CalibrationRuntime.Argument(option);if(value.Length>0){args.Add(option);args.Add(value);}}
   return args.ToArray();
  }
  static XDocument Document()=>new XDocument(new XDeclaration("1.0","UTF-8",null),new XDocumentType("plist","-//Apple//DTD PLIST 1.0//EN","http://www.apple.com/DTDs/PropertyList-1.0.dtd",null),new XElement("plist",new XAttribute("version","1.0"),new XElement("dict",new XElement("key","Label"),new XElement("string",Label),new XElement("key","ProgramArguments"),new XElement("array",Arguments().Select(x=>new XElement("string",x))),new XElement("key","RunAtLoad"),new XElement("true"))));
  public static bool Enabled=>File.Exists(FilePath)&&XNode.DeepEquals(XDocument.Load(FilePath).Root,Document().Root);
  public static void Set(bool enabled){
   if(File.Exists(FilePath)&&!Enabled)throw new IOException("已有不同的同名登录项，请先核对："+FilePath);
   if(!enabled){if(File.Exists(FilePath))File.Delete(FilePath);return;}
   Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temp=FilePath+".tmp";Document().Save(temp);if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);
  }
 }
}

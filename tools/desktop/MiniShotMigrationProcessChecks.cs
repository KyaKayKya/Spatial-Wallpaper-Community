using System;using System.IO;using System.Diagnostics;using System.Threading;using SpatialWallpaper.Content;
static class MiniShotMigrationProcessChecks {
 static int checks;static void Yes(bool value,string name){if(!value)throw new Exception(name);checks++;}
 static bool Gone(Process process){try{return process.HasExited;}catch(InvalidOperationException){return true;}}
 static Process Start(string mode,string file){return Process.Start(new ProcessStartInfo(typeof(MiniShotMigrationProcessChecks).Assembly.Location,"--helper "+mode+" \""+file+"\""){UseShellExecute=false,CreateNoWindow=true});}
 static int Main(string[] args){
  if(args.Length==3&&args[0]=="--helper"){if(args[1]=="committed")File.WriteAllText(args[2],"committed");if(args[1]=="exit")return 0;Thread.Sleep(5000);File.WriteAllText(args[2],"late-commit");return 0;}
  try{Directory.CreateDirectory(args[0]);
   foreach(string mode in new[]{"pending","committed"}){string file=Path.Combine(args[0],mode+".json");using(var process=Start(mode,file)){
    if(mode=="committed"){var clock=Stopwatch.StartNew();while(!File.Exists(file)&&clock.ElapsedMilliseconds<2000)Thread.Sleep(5);Yes(File.Exists(file),"fixture crossed commit before deadline");}
    bool timedOut=false;try{MiniShotMigrationProcess.Wait(process,40,2000);}catch(TimeoutException error){timedOut=true;Yes(error.Message.Contains("已终止")&&error.Message.Contains("可能已提交")&&error.Message.Contains("旧存档保留"),"timeout communicates commit uncertainty");}
    Yes(timedOut&&Gone(process),"owned helper exit proven");Yes(mode=="committed"?File.ReadAllText(file)=="committed":!File.Exists(file),"timeout retains existing commit and prevents late commit");
   }}
   using(var process=Start("exit",Path.Combine(args[0],"exit.json"))){Yes(process.WaitForExit(2000),"fixture exits normally");MiniShotMigrationProcess.Wait(process,1);Yes(process.ExitCode==0,"already exited race accepted");}
   Console.WriteLine("migration process checks passed: "+checks);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

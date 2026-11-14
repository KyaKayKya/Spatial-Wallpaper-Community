using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintImportResult {public BlueprintDocument Document;public CompiledBlueprint Compiled;public bool CacheHit;public long CacheBytes;public string SourcePath;}
 // The helper is trusted product code, with bounded input, memory and wall time.
 // This is resource isolation, not an AppContainer permission boundary.
 public static class BlueprintImportProcess {
  static string Quote(string value){var result=new StringBuilder("\"");int slashes=0;foreach(char ch in value){if(ch=='\\'){slashes++;continue;}if(ch=='"'){result.Append('\\',slashes*2+1).Append(ch);slashes=0;continue;}result.Append('\\',slashes).Append(ch);slashes=0;}return result.Append('\\',slashes*2).Append('"').ToString();}
  public static BlueprintImportResult Run(string executable,string source,string catalogJson,string root,CancellationToken token){
   string jobs=Path.GetFullPath(Path.Combine(root,"jobs"));string job=Path.Combine(jobs,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);Process child=null;ImportJob limit=null;
   try{File.WriteAllText(Path.Combine(job,"catalog.json"),catalogJson);string cache=Path.Combine(root,"cache");var start=new ProcessStartInfo(executable,string.Join(" ",new[]{Path.GetFullPath(source),job,cache}.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(executable)};
    child=Process.Start(start);if(child==null)throw new BlueprintImportException("import-worker-start-failed");limit=new ImportJob(child);var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();var deadline=Stopwatch.StartNew();while(!child.WaitForExit(50)){token.ThrowIfCancellationRequested();if(deadline.Elapsed.TotalSeconds>30)throw new BlueprintImportException("import-worker-time-budget");}token.ThrowIfCancellationRequested();string response=output.GetAwaiter().GetResult();string stderr=error.GetAwaiter().GetResult();if(response.Length>16384||stderr.Length>16384)throw new BlueprintImportException("import-worker-output-budget");if(string.IsNullOrWhiteSpace(response))throw new BlueprintImportException("import-worker-exited: "+child.ExitCode);var report=JObject.Parse(response);if(child.ExitCode!=0||report["error"]!=null)throw new BlueprintImportException((string)report["error"]??"import-worker-failed");string key=(string)report["key"];var file=new FileInfo(Path.Combine(job,"document.bin"));if(!file.Exists||file.Length>NbtInput.MaxInputBytes)throw new BlueprintImportException("import-worker-document-budget");var document=BlueprintDocumentTransport.Read(File.ReadAllBytes(file.FullName));var catalog=ModelCatalog.Load(catalogJson);if(key!=BlockMeshCompiler.CacheKeyFor(document.SourceHash,catalog.Fingerprint))throw new BlueprintImportException("import-worker-identity");long bytes;var compiled=BlueprintCacheFiles.Read(cache,key,out bytes);if(compiled==null)throw new BlueprintImportException("import-worker-cache-invalid");return new BlueprintImportResult{Document=document,Compiled=compiled,CacheHit=(bool)report["cache_hit"],CacheBytes=bytes,SourcePath=BlueprintSourceSnapshot.Preserve(root,document.SourceHash,Path.Combine(job,"source.bin"),token)};
   }finally{if(child!=null){try{if(!child.HasExited){child.Kill();child.WaitForExit(5000);}}finally{limit?.Dispose();child.Dispose();}}string resolved=Path.GetFullPath(job);if(resolved.StartsWith(jobs+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileName(resolved),"N",out _))Directory.Delete(resolved,true);}
  }
  sealed class ImportJob:IDisposable {
   IntPtr handle;
   [StructLayout(LayoutKind.Sequential)]struct Basic {public long processTime,jobTime;public uint flags;public UIntPtr minimum,maximum;public uint active;public UIntPtr affinity;public uint priority,scheduling;}
   [StructLayout(LayoutKind.Sequential)]struct Io {public ulong read,write,other,readBytes,writeBytes,otherBytes;}
   [StructLayout(LayoutKind.Sequential)]struct Limits {public Basic basic;public Io io;public UIntPtr processMemory,jobMemory,peakProcess,peakJob;}
   [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObjectW(IntPtr attributes,string name);
   [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int kind,ref Limits limits,uint size);
   [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
   [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
   public ImportJob(Process child){handle=CreateJobObjectW(IntPtr.Zero,null);var limits=new Limits{basic=new Basic{flags=0x2000|0x100|8,active=1},processMemory=new UIntPtr(1024UL*1024*1024)};if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))||!AssignProcessToJobObject(handle,child.Handle)){Dispose();throw new BlueprintImportException("import-worker-resource-policy: "+Marshal.GetLastWin32Error());}}
   public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
  }
 }
}

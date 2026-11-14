"""Experimental Host build with bounded Worker log capture; never a release artifact."""
from pathlib import Path
import sys,subprocess,json,hashlib
from check_contracts import ROOT,DATA
from windows_framework import references
out=Path(sys.argv[1]).resolve()
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--output',str(out)],check=True)
source=ROOT/'Supervisor/Hosting/WindowsWorkerLauncher.cs'
text=source.read_text()
needle='lock(gate){int keep=Math.Min(count,log.Length);'
assert text.count(needle)==1
capture='string capture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"experimental-worker-"+ProcessId+".log");using(var file=new FileStream(capture,FileMode.Append,FileAccess.Write,FileShare.Read)){int remaining=(int)Math.Max(0,262144-file.Length);if(remaining>0)file.Write(bytes,0,Math.Min(count,remaining));}'
patched=out/'ExperimentalWindowsWorkerLauncher.cs';patched.write_text(text.replace(needle,capture+needle))
host_source=ROOT/'Supervisor/Host.cs'
host=host_source.read_text()
trace='static void ShutdownTrace(string stage){try{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"experimental-shutdown.jsonl");File.AppendAllText(path,new JObject{{"stage",stage},{"qpc",Stopwatch.GetTimestamp()},{"frequency",Stopwatch.Frequency},{"thread",Thread.CurrentThread.ManagedThreadId}}.ToString(Formatting.None)+Environment.NewLine);}catch{}}\n'
replacements={
 '  static JObject ReadJson(': '  '+trace+'  static JObject ReadJson(',
 'public void Dispose(){exit.Set();acceptor.Join(3000);}': 'public void Dispose(){ShutdownTrace("service-close-begin");exit.Set();bool joined=acceptor.Join(3000);ShutdownTrace(joined?"service-close-end":"service-close-timeout");}',
 'finally{runtime.Close();if(!runtime.Completion.Wait(5000))Environment.Exit(2);runtime.Completion.GetAwaiter().GetResult();gpu.Close();}': 'finally{ShutdownTrace("runtime-close-begin");runtime.Close();if(!runtime.Completion.Wait(5000)){ShutdownTrace("runtime-close-timeout");Environment.Exit(2);}runtime.Completion.GetAwaiter().GetResult();ShutdownTrace("runtime-close-end");gpu.Close();ShutdownTrace("gpu-close-end");}',
 'finally{storage.Close();Wait(storage.Completion);llm.Dispose();Wait(llm.Completion);}': 'finally{ShutdownTrace("storage-close-begin");storage.Close();Wait(storage.Completion);ShutdownTrace("storage-close-end");ShutdownTrace("llm-close-begin");llm.Dispose();Wait(llm.Completion);ShutdownTrace("llm-close-end");}',
 'finally{journal.Close();Wait(journal.Completion);}': 'finally{ShutdownTrace("journal-close-begin");journal.Close();Wait(journal.Completion);ShutdownTrace("journal-close-end");}',
 'File.Delete(Path.Combine(state,"endpoint.json"));return 0;': 'File.Delete(Path.Combine(state,"endpoint.json"));ShutdownTrace("host-return-success");return 0;',
 'Console.Error.WriteLine("Supervisor failed: "+error.GetBaseException().Message);return 1;': 'ShutdownTrace("host-return-error");Console.Error.WriteLine("Supervisor failed: "+error.GetBaseException().Message);return 1;'
}
for before,after in replacements.items():
 assert host.count(before)==1,('diagnostic Host source drift',before)
 host=host.replace(before,after)
patched_host=out/'ExperimentalHost.cs';patched_host.write_text(host)
store_source=ROOT/'Supervisor/Broker/PrivateStore.cs';store=store_source.read_text()
store_needle='  public long Commit(BrokerSession session,long expectedVersion,IDictionary<string,byte[]> changes){'
assert store.count(store_needle)==1
store_wrapper='''  string diagnosticStage;
  public long Commit(BrokerSession session,long expectedVersion,IDictionary<string,byte[]> changes){diagnosticStage="begin";try{return DiagnosticCommit(session,expectedVersion,changes);}catch(Exception error){try{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"experimental-storage.jsonl");if(!File.Exists(path)||new FileInfo(path).Length<32768)File.AppendAllText(path,"stage="+diagnosticStage+" type="+error.GetType().Name+" hresult="+error.HResult+" native="+(error is Win32Exception?(error as Win32Exception).NativeErrorCode:0)+Environment.NewLine);}catch{}throw;}}
  long DiagnosticCommit(BrokerSession session,long expectedVersion,IDictionary<string,byte[]> changes){'''
store=store.replace(store_needle,store_wrapper)
for before,after in {
 'var old=Load(directory);':'diagnosticStage="load";var old=Load(directory);',
 'using(var output=new FileStream(temporary,':'diagnosticStage="write-temp";using(var output=new FileStream(temporary,',
 'session.BeginCommit();string target=':'diagnosticStage="commit-session";session.BeginCommit();diagnosticStage="target-check";string target=',
 'AtomicReplace(temporary,target);':'diagnosticStage="replace";AtomicReplace(temporary,target);',
 'try{if(File.Exists(temporary))File.Delete(temporary);}':'try{string savedStage=diagnosticStage;diagnosticStage="delete-temp-after-"+savedStage;if(File.Exists(temporary))File.Delete(temporary);diagnosticStage=savedStage;}'
}.items():
 assert store.count(before)==1,('diagnostic store source drift',before)
 store=store.replace(before,after)
patched_store=out/'ExperimentalPrivateStore.cs';patched_store.write_text(store)
files=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerExitCodes.cs','WorkerFrameCodec.cs','RuntimeLimits.Generated.cs']]+[ROOT/'Supervisor/Host.cs']
files+=list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs'))
files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs')
for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker']:files+=list((ROOT/folder).glob('*.cs'))
files=[patched if p==source else patched_host if p==host_source else patched_store if p==store_source else p for p in files]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-target:winexe','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Supervisor.exe'),*map(str,files)],check=True)
report=json.loads((out/'build.json').read_text());report.update(status='diagnostic-only-not-production-acceptance',release_candidate=False,experimental_source_sha256=hashlib.sha256(patched.read_bytes()).hexdigest());report['files']['Supervisor.exe']=hashlib.sha256((out/'Supervisor.exe').read_bytes()).hexdigest()
report['experimental_host_source_sha256']=hashlib.sha256(patched_host.read_bytes()).hexdigest()
report['experimental_store_source_sha256']=hashlib.sha256(patched_store.read_bytes()).hexdigest()
(out/'build.json').write_text(json.dumps(report,indent=2))

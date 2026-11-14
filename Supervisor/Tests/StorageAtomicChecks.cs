using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Broker;

static class StorageAtomicChecks {
 static void Need(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static Dictionary<string,byte[]> Value(int n)=>new Dictionary<string,byte[]>{{"value",BitConverter.GetBytes(n)}};
 static byte[] Snapshot(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var m=new MemoryStream()){f.CopyTo(m);return m.ToArray();}}
 static long Validate(byte[] bytes){
  if(bytes.Length<52)throw new Exception("short-snapshot");
  using(var sha=SHA256.Create()){byte[] digest=sha.ComputeHash(bytes,0,bytes.Length-32);for(int i=0;i<32;i++)if(digest[i]!=bytes[bytes.Length-32+i])throw new Exception("torn-snapshot");}
  using(var stream=new MemoryStream(bytes,0,bytes.Length-32,false))using(var reader=new BinaryReader(stream)){
   NeedFormat(reader.ReadUInt64()==0x31564b5350575353UL);long version=reader.ReadInt64();NeedFormat(reader.ReadInt32()==1&&reader.ReadInt32()==5&&reader.ReadInt32()==4);
   NeedFormat(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(5))=="value"&&reader.ReadInt32()==version&&stream.Position==stream.Length);return version;
  }
 }
 static void NeedFormat(bool valid){if(!valid)throw new Exception("snapshot-format-or-value-version-mismatch");}
 public static void Run(string root){
  Directory.CreateDirectory(root);
  using(var journal=new FaultJournal(Path.Combine(root,"journal")))using(var store=new PrivateStore(Path.Combine(root,"store"))){
   Guid installation=Guid.NewGuid();var session=new BrokerSession(journal.Begin(installation,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"private-storage"});
   string path=Path.Combine(root,"store","live-"+installation.ToString("N"),"state.bin");store.Commit(session,0,Value(1));
   using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
    Need(store.Commit(session,1,Value(2))==2,"delete-sharing-reader-permits-replace");
    using(var memory=new MemoryStream()){held.CopyTo(memory);Need(Validate(memory.ToArray())==1,"old-reader-retains-old-complete-file");}
    Need(Validate(Snapshot(path))==2,"new-reader-sees-new-file");
    Need(store.Commit(session,2,Value(3))==3,"multiple-replacements-with-old-reader");
   }
   int stop=0,reads=0;Exception readFailure=null;var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   var observer=Task.Run(()=>{try{long previous=0;while(Volatile.Read(ref stop)==0){long version=Validate(Snapshot(path));if(version<previous)throw new Exception("snapshot-version-regressed");previous=version;Interlocked.Increment(ref reads);ready.TrySetResult(true);}}catch(Exception e){readFailure=e;ready.TrySetException(e);}});
   try{Need(ready.Task.Wait(1000),"observer-started");for(int i=4;i<=1003;i++)store.Commit(session,i-1,Value(i));}
   finally{Volatile.Write(ref stop,1);Need(observer.Wait(3000),"observer-stopped");}
   if(readFailure!=null)throw readFailure;
   Need(reads>0&&store.Read(session).Version==1003&&BitConverter.ToInt32(store.Read(session).Get("value"),0)==1003,"concurrent-observer-and-1000-atomic-commits");Console.WriteLine("SNAPSHOT_READS "+reads);
   byte[] intact=Snapshot(path);bool rejected=false;
   File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly);
   try{store.Commit(session,1003,Value(1004));}catch(System.ComponentModel.Win32Exception e){Need(e.NativeErrorCode==5,"readonly-denied-access");rejected=true;}
   finally{File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);}
   Need(rejected&&Snapshot(path).SequenceEqual(intact),"readonly-target-preserves-old-state");
   Need(!Directory.GetFiles(Path.GetDirectoryName(path),"write-*.tmp").Any(),"failed-replace-cleans-temporary");
   Need(store.Commit(session,1003,Value(1004))==1004&&Validate(Snapshot(path))==1004,"commit-recovers-after-readonly-rejection");
   intact=Snapshot(path);string temporary=null;Exception primary=null;
   PrivateStore.BeforeReplace=()=>{temporary=Directory.GetFiles(Path.GetDirectoryName(path),"write-*.tmp").Single();File.SetAttributes(temporary,File.GetAttributes(temporary)|FileAttributes.ReadOnly);File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly);};
   try{store.Commit(session,1004,Value(1005));}catch(Exception error){primary=error;}
   finally{PrivateStore.BeforeReplace=null;if(temporary!=null&&File.Exists(temporary))File.SetAttributes(temporary,File.GetAttributes(temporary)&~FileAttributes.ReadOnly);File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);}
   Need(primary is System.ComponentModel.Win32Exception&&((System.ComponentModel.Win32Exception)primary).NativeErrorCode==5,"double-fault-preserves-primary-native-error");
   Need(primary.Data["private-store-cleanup-error"] is UnauthorizedAccessException,"double-fault-retains-cleanup-error");
   Need(Snapshot(path).SequenceEqual(intact)&&File.Exists(temporary),"double-fault-preserves-old-and-owned-temporary");
  }
  using(var recovered=new PrivateStore(Path.Combine(root,"store")))Need(!Directory.GetFiles(Path.Combine(root,"store"),"write-*.tmp",SearchOption.AllDirectories).Any(),"restart-reclaims-double-fault-temporary");
  Console.WriteLine("ENVIRONMENT bits="+(IntPtr.Size*8)+" os="+Environment.OSVersion+" filesystem="+new DriveInfo(Path.GetPathRoot(root)).DriveFormat);
 }
}

using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;using System.Security.Cryptography;using System.Runtime.InteropServices;using System.ComponentModel;using System.Security.AccessControl;using System.Security.Principal;
namespace SpatialWallpaper.Supervision.Broker {
 public sealed class StoreQuotaException:InvalidOperationException{public StoreQuotaException():base("private-store-quota"){}}
 public sealed class StoreConflictException:InvalidOperationException{public StoreConflictException():base("private-store-version-conflict"){}}
 public sealed class StoreSnapshot {
  readonly Dictionary<string,byte[]> values;public long Version{get;}public long ChargedBytes{get;}
  internal Dictionary<string,byte[]> Values=>values;
  internal StoreSnapshot(long version,Dictionary<string,byte[]> data,long charged){Version=version;values=data;ChargedBytes=charged;}
  public string[] Keys=>values.Keys.ToArray();
  internal int ValueBytes(string key)=>values.TryGetValue(key,out var data)?data.Length:0;
  public byte[] Get(string key){if(!values.TryGetValue(key,out var data))return null;return (byte[])data.Clone();}
 }
 // Synchronous bounded storage primitive. Run it on the Broker persistence lane, never on a Worker/UI/monitor thread.
 public sealed class PrivateStore:IDisposable {
  public const int QuotaBytes=16*1024*1024,MaxKeys=4096,MaxKeyBytes=256;
  const int MaxFileBytes=QuotaBytes+MaxKeys*8+52;const ulong Magic=0x31564b5350575353UL;
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);readonly object gate=new object();readonly string root;readonly FileStream ownership;bool disposed;
#if SDK_SUPERVISOR_TESTS
  public static Action BeforeReplace,AfterReplace;
#endif
  static void NoLink(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("private-store-link");}
  static bool Temporary(string name)=>name.Length==42&&name.StartsWith("write-",StringComparison.Ordinal)&&name.EndsWith(".tmp",StringComparison.Ordinal)&&Guid.TryParseExact(name.Substring(6,32),"N",out var ignored);
  public PrivateStore(string directory){
   root=Path.GetFullPath(directory);Directory.CreateDirectory(root);NoLink(root);
   string marker=Path.Combine(root,".sdk-private-store");if(!File.Exists(marker)&&Directory.EnumerateFileSystemEntries(root).Any())throw new InvalidDataException("private-store-not-owned");
   string ownerPath=Path.Combine(root,".owner.lock");if(File.Exists(ownerPath))NoLink(ownerPath);
   ownership=new FileStream(ownerPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    try{
    if(File.Exists(marker)){NoLink(marker);if(new FileInfo(marker).Length>64)throw new InvalidDataException("private-store-marker-size");if(File.ReadAllText(marker)!="SpatialWallpaper.PrivateStore.1")throw new InvalidDataException("private-store-owner");}else File.WriteAllText(marker,"SpatialWallpaper.PrivateStore.1");
    if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("private-store-platform-unverified");
    var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(root,acl);
    foreach(string path in Directory.EnumerateDirectories(root)){CheckNamespace(path);var entries=Directory.GetFiles(path);if(entries.Length>33)throw new InvalidDataException("private-store-temp-count");foreach(string file in entries){NoLink(file);if(Temporary(Path.GetFileName(file)))File.Delete(file);else if(Path.GetFileName(file)!="state.bin")throw new InvalidDataException("private-store-unknown-file");}}
   }catch{ownership.Dispose();throw;}
  }
  void Live(){if(disposed)throw new ObjectDisposedException(nameof(PrivateStore));}
  void CheckNamespace(string path){
   string full=Path.GetFullPath(path),name=Path.GetFileName(full);if(!string.Equals(Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("private-store-path");
   string id=name.StartsWith("live-",StringComparison.Ordinal)?name.Substring(5):name.StartsWith("preview-",StringComparison.Ordinal)?name.Substring(8):"";
   if(!Guid.TryParseExact(id,"N",out var parsed)||parsed==Guid.Empty||id!=parsed.ToString("N"))throw new InvalidDataException("private-store-namespace");NoLink(full);
   if(Directory.EnumerateDirectories(full).Any())throw new InvalidDataException("private-store-nested-directory");
  }
  string Namespace(BrokerSession session,bool create){if(session==null)throw new ArgumentNullException(nameof(session));session.Demand("private-storage");string path=Path.Combine(root,(session.Preview?"preview-":"live-")+session.Installation.ToString("N"));if(create)Directory.CreateDirectory(path);if(Directory.Exists(path))CheckNamespace(path);return path;}
  static byte[] Key(string key){if(string.IsNullOrEmpty(key)||key.Length>MaxKeyBytes)throw new ArgumentException("private-store-key");byte[] bytes=Utf8.GetBytes(key);if(bytes.Length>MaxKeyBytes)throw new ArgumentException("private-store-key-size");return bytes;}
  static long Charge(Dictionary<string,byte[]> values){if(values.Count>MaxKeys)throw new StoreQuotaException();long size=0;foreach(var pair in values){if(pair.Value==null)throw new InvalidDataException("private-store-null-value");size=checked(size+Key(pair.Key).Length+pair.Value.LongLength);if(size>QuotaBytes)throw new StoreQuotaException();}return size;}
  static byte[] Exact(BinaryReader reader,int count){if(count<0||count>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("private-store-truncated");var value=reader.ReadBytes(count);if(value.Length!=count)throw new EndOfStreamException();return value;}
  StoreSnapshot Load(string directory){
   string path=Path.Combine(directory,"state.bin");if(!File.Exists(path))return new StoreSnapshot(0,new Dictionary<string,byte[]>(StringComparer.Ordinal),0);NoLink(path);
   byte[] bytes;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(file.Length<52||file.Length>MaxFileBytes)throw new InvalidDataException("private-store-file-size");bytes=new byte[(int)file.Length];int position=0;while(position<bytes.Length){int count=file.Read(bytes,position,bytes.Length-position);if(count==0)throw new EndOfStreamException();position+=count;}}
   using(var sha=SHA256.Create()){var digest=sha.ComputeHash(bytes,0,bytes.Length-32);int different=0;for(int i=0;i<32;i++)different|=digest[i]^bytes[bytes.Length-32+i];if(different!=0)throw new InvalidDataException("private-store-checksum");}
   using(var stream=new MemoryStream(bytes,0,bytes.Length-32,false))using(var reader=new BinaryReader(stream,Utf8)){
    if(reader.ReadUInt64()!=Magic)throw new InvalidDataException("private-store-format");long version=reader.ReadInt64();int count=reader.ReadInt32();if(version<1||count<0||count>MaxKeys)throw new InvalidDataException("private-store-header");
    var values=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
    for(int i=0;i<count;i++){int keyLength=reader.ReadInt32(),valueLength=reader.ReadInt32();if(keyLength<1||keyLength>MaxKeyBytes||valueLength<0||valueLength>QuotaBytes||(total+=keyLength+(long)valueLength)>QuotaBytes)throw new InvalidDataException("private-store-record-size");string key=Utf8.GetString(Exact(reader,keyLength));if(values.ContainsKey(key))throw new InvalidDataException("private-store-duplicate-key");values.Add(key,Exact(reader,valueLength));}
    if(stream.Position!=stream.Length)throw new InvalidDataException("private-store-tail");return new StoreSnapshot(version,values,total);
   }
  }
  public StoreSnapshot Read(BrokerSession session){lock(gate){Live();return Load(Namespace(session,false));}}
  public long Commit(BrokerSession session,long expectedVersion,IDictionary<string,byte[]> changes){
   if(expectedVersion<0||changes==null||changes.Count>MaxKeys*2)throw new ArgumentException("private-store-changes");
   lock(gate){Live();string directory=Namespace(session,true);var old=Load(directory);if(old.Version!=expectedVersion)throw new StoreConflictException();
    var next=new Dictionary<string,byte[]>(old.Values,StringComparer.Ordinal);foreach(var pair in changes){Key(pair.Key);if(pair.Value==null)next.Remove(pair.Key);else{if(pair.Value.LongLength>QuotaBytes)throw new StoreQuotaException();next[pair.Key]=pair.Value;}}Charge(next);
    // Snapshot caller buffers after preflight. No rejected oversized request allocates an additional payload copy.
    foreach(string key in next.Keys.ToArray())next[key]=(byte[])next[key].Clone();long revision=checked(old.Version+1);
     string temporary=Path.Combine(directory,"write-"+Guid.NewGuid().ToString("N")+".tmp");
     Exception primary=null;try{
     using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,4096,FileOptions.WriteThrough)){
      using(var writer=new BinaryWriter(output,Utf8,true)){writer.Write(Magic);writer.Write(revision);writer.Write(next.Count);foreach(var pair in next.OrderBy(p=>p.Key,StringComparer.Ordinal)){byte[] key=Key(pair.Key);writer.Write(key.Length);writer.Write(pair.Value.Length);writer.Write(key);writer.Write(pair.Value);}writer.Flush();}
      output.Position=0;byte[] digest;using(var hash=SHA256.Create())digest=hash.ComputeHash(output);output.Write(digest,0,digest.Length);output.Flush(true);
     }
#if SDK_SUPERVISOR_TESTS
     BeforeReplace?.Invoke();
#endif
     session.BeginCommit();string target=Path.Combine(directory,"state.bin");if(File.Exists(target))NoLink(target);AtomicReplace(temporary,target);
#if SDK_SUPERVISOR_TESTS
     AfterReplace?.Invoke();
#endif
     return revision;
    }catch(Exception error){primary=error;throw;}finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception cleanup){if(primary==null)throw;primary.Data["private-store-cleanup-error"]=cleanup;}}
   }
  }
  // Only the trusted uninstall coordinator calls this after revoking/stopping all sessions of the installation.
  public void Uninstall(Guid installation){if(installation==Guid.Empty)throw new ArgumentException("installation");lock(gate){Live();foreach(string prefix in new[]{"live-","preview-"}){string path=Path.Combine(root,prefix+installation.ToString("N"));if(!Directory.Exists(path))continue;CheckNamespace(path);var files=Directory.GetFiles(path);foreach(string file in files){NoLink(file);string name=Path.GetFileName(file);if(name!="state.bin"&&!Temporary(name))throw new InvalidDataException("uninstall-unknown-file");}foreach(string file in files)File.Delete(file);Directory.Delete(path,false);}}}
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;ownership.Dispose();}}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct RenameInformation {public uint Flags;public IntPtr Root;public uint NameBytes;public char FirstCharacter;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,int kind,IntPtr information,uint length);
  static void AtomicReplace(string source,string target){
   // The temporary's contents have already been flushed. This is the commit point:
   // existing readers retain their complete snapshot; new opens see the new file.
   // No fallback, readonly bypass, or claim of power-loss durability for directory metadata.
   byte[] name=Encoding.Unicode.GetBytes(Path.GetFullPath(target));
   int offset=(int)Marshal.OffsetOf(typeof(RenameInformation),nameof(RenameInformation.FirstCharacter));
   int length=checked(Math.Max(Marshal.SizeOf(typeof(RenameInformation)),offset+name.Length+2));IntPtr buffer=Marshal.AllocHGlobal(length);
   try{
    Marshal.StructureToPtr(new RenameInformation{Flags=3,Root=IntPtr.Zero,NameBytes=(uint)name.Length},buffer,false); // REPLACE_IF_EXISTS | POSIX_SEMANTICS
    Marshal.Copy(name,0,IntPtr.Add(buffer,offset),name.Length);Marshal.WriteInt16(buffer,offset+name.Length,0);
    using(var handle=CreateFile(source,0x10000,1|2|4,IntPtr.Zero,3,0,IntPtr.Zero)){
     if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
     if(!SetFileInformationByHandle(handle,22,buffer,(uint)length))throw new Win32Exception(Marshal.GetLastWin32Error()); // FileRenameInfoEx
    }
   }finally{Marshal.FreeHGlobal(buffer);}
  }
 }
}

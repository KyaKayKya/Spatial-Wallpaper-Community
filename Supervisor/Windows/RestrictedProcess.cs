using System;using System.Threading;using System.IO;using System.Text;using System.Collections.Generic;using System.ComponentModel;using System.Runtime.InteropServices;using System.Security.AccessControl;using System.Security.Principal;
namespace SpatialWallpaper.Supervision.Windows {
 // Construct once during background-process startup, before creating any UI. The UI/tray presenter is separate.
 public sealed class PrivateStation:IDisposable {
  internal IntPtr Handle;readonly IntPtr previous;internal readonly string Name;int children;internal int ChildCount=>Volatile.Read(ref children);internal void ChildCreated()=>Interlocked.Increment(ref children);internal void ChildReleased()=>Interlocked.Decrement(ref children);
  internal static void Check(bool value){if(!value)throw new Win32Exception(Marshal.GetLastWin32Error());}
  public PrivateStation(){
   previous=Native.GetProcessWindowStation();Handle=Native.CreateWindowStationW(null,0,0x10000000,IntPtr.Zero);Check(Handle!=IntPtr.Zero);
   try{var name=new StringBuilder(512);uint needed;Check(Native.GetUserObjectInformationW(Handle,2,name,1024,out needed));Name=name.ToString();Check(!string.Equals(Name,"WinSta0",StringComparison.OrdinalIgnoreCase));Check(Native.SetProcessWindowStation(Handle));}
   catch{Native.CloseWindowStation(Handle);Handle=IntPtr.Zero;throw;}
  }
  public void Dispose(){if(Handle==IntPtr.Zero)return;if(ChildCount!=0)throw new InvalidOperationException("private-station-has-workers");Check(Native.SetProcessWindowStation(previous));Check(Native.CloseWindowStation(Handle));Handle=IntPtr.Zero;}
 }
 public sealed class RestrictedProcess:IDisposable {
  IntPtr profile,job,process,desktop;readonly string profileName;readonly PrivateStation station;bool disposed;
  public int ProcessId{get;private set;}
  // Cached from CreateProcess's original handle before resume; remains valid after Dispose.
  public long CreationFileTime{get;private set;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long creation,out long exit,out long kernel,out long user);
  public static long ReadCreationFileTime(IntPtr handle){
   if(handle==IntPtr.Zero||handle==new IntPtr(-1))throw new ArgumentException("process-identity-handle");
   long creation,exit,kernel,user;PrivateStation.Check(GetProcessTimes(handle,out creation,out exit,out kernel,out user));
   if(creation<=0)throw new InvalidDataException("process-creation-time");return creation;
  }
  internal string ProfileName=>profileName;
  RestrictedProcess(PrivateStation owner,Guid attempt){station=owner;profileName="SpatialWallpaper.Worker."+(attempt==Guid.Empty?Guid.NewGuid():attempt).ToString("N");}
  static IntPtr Pack<T>(T value,List<IntPtr> memory)where T:struct{IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));memory.Add(p);Marshal.StructureToPtr(value,p,false);return p;}
  static void Attribute(IntPtr list,ulong kind,IntPtr value,int size)=>PrivateStation.Check(Native.UpdateProcThreadAttribute(list,0,new UIntPtr(kind),value,new UIntPtr((uint)size),IntPtr.Zero,IntPtr.Zero));
  static void Hr(int value){if(value<0)Marshal.ThrowExceptionForHR(value);}
  internal static string ProfileDirectory(string sid){
   IntPtr value;Hr(Native.GetAppContainerFolderPath(sid,out value));
   try{return Path.GetFullPath(Marshal.PtrToStringUni(value));}finally{Marshal.FreeCoTaskMem(value);}
  }
  static void RestrictProfile(string name,string sid){
   // These are newly created OS profile resources, never a caller-selected path.
   string ac=ProfileDirectory(sid),root=Path.GetDirectoryName(ac);
   string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
   if(!string.Equals(Path.GetDirectoryName(root),packages,StringComparison.OrdinalIgnoreCase)||
      !string.Equals(Path.GetFileName(root),name,StringComparison.OrdinalIgnoreCase)||
      !string.Equals(Path.GetFileName(ac),"AC",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("unexpected-profile-path");
   var caller=WindowsIdentity.GetCurrent().User;var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);var package=new SecurityIdentifier(sid);
   var paths=new List<string>{root};
   for(int i=0;i<paths.Count;i++){
    var attr=File.GetAttributes(paths[i]);if((attr&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("profile-reparse-point");
    if((attr&FileAttributes.Directory)!=0)paths.AddRange(Directory.GetFileSystemEntries(paths[i]));
    if(paths.Count>4096)throw new InvalidDataException("profile-file-count");
   }
   foreach(string path in paths){
    bool directory=Directory.Exists(path);FileSystemSecurity acl=directory?(FileSystemSecurity)new DirectorySecurity():new FileSecurity();acl.SetAccessRuleProtection(true,false);
    var inherit=directory?InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit:InheritanceFlags.None;
    acl.AddAccessRule(new FileSystemAccessRule(caller,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(system,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(package,FileSystemRights.ReadAndExecute,inherit,PropagationFlags.None,AccessControlType.Allow));
    if(directory)Directory.SetAccessControl(path,(DirectorySecurity)acl);else File.SetAccessControl(path,(FileSecurity)acl);
   }
   string keyPath=@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\"+name;
   var registryPaths=new List<string>{keyPath};
   for(int i=0;i<registryPaths.Count;i++)using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPaths[i],true)){
    if(key==null)throw new InvalidDataException("missing-new-profile-registry");
    // Windows may provision descendants. Protect every existing key, including explicit child ACEs.
    foreach(string child in key.GetSubKeyNames()){
     string next=registryPaths[i]+(char)92+child;
     if(next.Length-keyPath.Length>2048||registryPaths.Count>=4096)throw new InvalidDataException("profile-registry-size");registryPaths.Add(next);
    }
    var acl=new RegistrySecurity();acl.SetAccessRuleProtection(true,false);
    acl.AddAccessRule(new RegistryAccessRule(caller,RegistryRights.FullControl,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new RegistryAccessRule(system,RegistryRights.FullControl,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new RegistryAccessRule(package,RegistryRights.ReadKey,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
    key.SetAccessControl(acl);
   }
  }
  static string Quote(string value){
   if(value==null||value.IndexOf((char)0)>=0)throw new ArgumentException("command-argument");
   var result=new StringBuilder();result.Append((char)34);int slashes=0;
   foreach(char character in value){if(character==(char)92){slashes++;continue;}if(character==(char)34)result.Append((char)92,slashes*2+1);else result.Append((char)92,slashes);result.Append(character);slashes=0;}
   result.Append((char)92,slashes*2).Append((char)34);return result.ToString();
  }
  static void Permissions(string root,string sid){
   var caller=WindowsIdentity.GetCurrent().User;var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);var package=new SecurityIdentifier(sid);var restricted=new SecurityIdentifier("S-1-15-2-2");
   // Caller supplies only a freshly generated, owned runtime copy. No ACL changes on source/system directories.
   if(!File.Exists(Path.Combine(root,".sdk-worker-owned")))throw new InvalidOperationException("worker-directory-not-owned");
   // Inspect each directory before traversal; recursive enumeration could follow a junction first.
   var paths=new List<string>{root};
   for(int i=0;i<paths.Count;i++){
    string path=paths[i];var attributes=File.GetAttributes(path);
    if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("runtime-reparse-point");
    if((attributes&FileAttributes.Directory)!=0){
     foreach(string entry in Directory.GetFileSystemEntries(path)){
      string full=Path.GetFullPath(entry);if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("runtime-path");
      paths.Add(full);if(paths.Count>8192)throw new InvalidDataException("runtime-file-count");
     }
    }else using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
     Native.FileInformation information;PrivateStation.Check(Native.GetFileInformationByHandle(file.SafeFileHandle.DangerousGetHandle(),out information));
     if(information.links!=1)throw new InvalidDataException("runtime-hardlink");
    }
   }
   foreach(string path in paths){
    var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("runtime-reparse-point");
    bool directory=(attributes&FileAttributes.Directory)!=0;
    FileSystemSecurity acl=directory?(FileSystemSecurity)new DirectorySecurity():new FileSecurity();acl.SetAccessRuleProtection(true,false);
    var inherit=directory?InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit:InheritanceFlags.None;
    acl.AddAccessRule(new FileSystemAccessRule(caller,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(system,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(package,FileSystemRights.ReadAndExecute,inherit,PropagationFlags.None,AccessControlType.Allow));
    if(!directory&&path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))acl.AddAccessRule(new FileSystemAccessRule(restricted,FileSystemRights.ReadAndExecute,AccessControlType.Allow));
    if(directory)Directory.SetAccessControl(path,(DirectorySecurity)acl);else File.SetAccessControl(path,(FileSecurity)acl);
   }
  }
  public static RestrictedProcess Start(PrivateStation station,string executable,string[] arguments,string ownedRoot,long memoryBytes,Func<string,IntPtr[]> prepare,Action<int> created,Guid attempt=default,Func<IntPtr> standardOutput=null,Action<Exception> cleanupFailure=null){
   if(station==null||station.Handle==IntPtr.Zero||IntPtr.Size!=8||memoryBytes<16777216||memoryBytes>4294967296)throw new ArgumentException("restricted-launch-policy");
   string root=Path.GetFullPath(ownedRoot),exe=Path.GetFullPath(executable);
   if(!exe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(exe))throw new InvalidDataException("worker-executable-location");
   var child=new RestrictedProcess(station,attempt);var memory=new List<IntPtr>();var capabilityMemory=new List<IntPtr>();IntPtr attributes=IntPtr.Zero,descriptor=IntPtr.Zero,environment=IntPtr.Zero;bool attributesReady=false;Native.ProcessInfo info=default;
   try{
    Hr(Native.CreateAppContainerProfile(child.profileName,child.profileName,"Spatial Wallpaper isolated renderer",IntPtr.Zero,0,out child.profile));
    IntPtr sidText;PrivateStation.Check(Native.ConvertSidToStringSidW(child.profile,out sidText));string sid;
    try{sid=Marshal.PtrToStringUni(sidText);}finally{Native.LocalFree(sidText);}
    RestrictProfile(child.profileName,sid);
    Directory.CreateDirectory(Path.Combine(root,"scratch"));Directory.CreateDirectory(Path.Combine(root,"profile"));Permissions(root,sid);
    var inherited=prepare==null?new IntPtr[0]:prepare(sid);if(inherited==null||inherited.Length>8)throw new ArgumentException("inherited-handle-count");
    string caller=WindowsIdentity.GetCurrent().User.Value;
    PrivateStation.Check(Native.ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;SY)(A;;GA;;;"+caller+")(A;;GA;;;"+sid+")S:(ML;;NW;;;LW)",1,out descriptor,IntPtr.Zero));
    var sa=new Native.SecurityAttributes{length=Marshal.SizeOf(typeof(Native.SecurityAttributes)),descriptor=descriptor};
    string desktopName="SpatialWallpaper_"+Guid.NewGuid().ToString("N");child.desktop=Native.CreateDesktopW(desktopName,IntPtr.Zero,IntPtr.Zero,0,0x10000000,ref sa);PrivateStation.Check(child.desktop!=IntPtr.Zero);
    UIntPtr size=UIntPtr.Zero;Native.InitializeProcThreadAttributeList(IntPtr.Zero,5,0,ref size);attributes=Marshal.AllocHGlobal(checked((int)size.ToUInt64()));PrivateStation.Check(Native.InitializeProcThreadAttributeList(attributes,5,0,ref size));attributesReady=true;
    IntPtr groups,values;uint groupCount,capCount;PrivateStation.Check(Native.DeriveCapabilitySidsFromName("registryRead",out groups,out groupCount,out values,out capCount));
    for(int i=0;i<groupCount;i++)capabilityMemory.Add(Marshal.ReadIntPtr(groups,i*IntPtr.Size));
    for(int i=0;i<capCount;i++)capabilityMemory.Add(Marshal.ReadIntPtr(values,i*IntPtr.Size));
    capabilityMemory.Add(groups);capabilityMemory.Add(values);
    if(capCount<1||capCount>16)throw new InvalidOperationException("capability-derivation");
    int itemSize=Marshal.SizeOf(typeof(Native.SidAttribute));IntPtr caps=Marshal.AllocHGlobal(checked(itemSize*(int)capCount));memory.Add(caps);
    for(int i=0;i<capCount;i++)Marshal.StructureToPtr(new Native.SidAttribute{sid=Marshal.ReadIntPtr(values,i*IntPtr.Size),attributes=4},IntPtr.Add(caps,i*itemSize),false);
    Attribute(attributes,0x20009,Pack(new Native.Capabilities{sid=child.profile,capabilities=caps,count=capCount},memory),Marshal.SizeOf(typeof(Native.Capabilities)));
    Attribute(attributes,0x2000f,Pack(1U,memory),4);
    Attribute(attributes,0x20007,Pack((1UL<<52)|(1UL<<56),memory),8);
    if(inherited.Length>0){IntPtr handles=Marshal.AllocHGlobal(inherited.Length*IntPtr.Size);memory.Add(handles);for(int i=0;i<inherited.Length;i++){if(inherited[i]==IntPtr.Zero||inherited[i]==new IntPtr(-1))throw new ArgumentException("invalid-inherited-handle");Marshal.WriteIntPtr(handles,i*IntPtr.Size,inherited[i]);}Attribute(attributes,0x20002,handles,inherited.Length*IntPtr.Size);}
    child.job=Native.CreateJobObjectW(IntPtr.Zero,null);PrivateStation.Check(child.job!=IntPtr.Zero);
    var limits=new Native.Limits{basic=new Native.BasicLimits{flags=0x2000|8|0x100,active=1},processMemory=new UIntPtr((ulong)memoryBytes)};
    IntPtr limitData=Pack(limits,memory);PrivateStation.Check(Native.SetInformationJobObject(child.job,9,limitData,(uint)Marshal.SizeOf(typeof(Native.Limits))));
    PrivateStation.Check(Native.SetInformationJobObject(child.job,4,Pack(0xffU,memory),4));
    Attribute(attributes,0x2000d,Pack(child.job,memory),IntPtr.Size);
    var command=new StringBuilder(Quote(exe));foreach(string argument in arguments)command.Append(' ').Append(Quote(argument));
    string systemRoot=Environment.GetEnvironmentVariable("SystemRoot");if(string.IsNullOrEmpty(systemRoot))throw new InvalidOperationException("missing-system-root");
    var variables=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"SystemRoot",systemRoot},{"WINDIR",systemRoot},{"LOCALAPPDATA",Path.Combine(root,"profile")},{"TEMP",Path.Combine(root,"scratch")},{"TMP",Path.Combine(root,"scratch")}};
    var block=new StringBuilder();foreach(var pair in variables)block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');block.Append('\0');environment=Marshal.StringToHGlobalUni(block.ToString());
    var startup=new Native.StartupEx{startup=new Native.Startup{cb=(uint)Marshal.SizeOf(typeof(Native.StartupEx)),desktop=string.Concat(station.Name,(char)92,desktopName)},attributes=attributes};
    if(standardOutput!=null){startup.startup.flags|=0x100;startup.startup.output=startup.startup.error=standardOutput();if(startup.startup.output==IntPtr.Zero)throw new ArgumentException("worker-standard-output");}
    PrivateStation.Check(Native.CreateProcessW(exe,command,IntPtr.Zero,IntPtr.Zero,inherited.Length>0,0x80000|4|0x08000000|0x400,environment,root,ref startup,out info));
    child.process=info.process;child.ProcessId=(int)info.pid;station.ChildCreated();
    child.CreationFileTime=ReadCreationFileTime(child.process);
    if(created!=null)created(child.ProcessId);
    if(Native.ResumeThread(info.thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
    return child;
   }catch{try{child.Dispose();}catch(Exception error){cleanupFailure?.Invoke(error);throw;}throw;}
   finally{
    if(info.thread!=IntPtr.Zero)Native.CloseHandle(info.thread);
    if(attributesReady)Native.DeleteProcThreadAttributeList(attributes);if(attributes!=IntPtr.Zero)Marshal.FreeHGlobal(attributes);
    foreach(var pointer in memory)Marshal.FreeHGlobal(pointer);foreach(var pointer in capabilityMemory)Native.LocalFree(pointer);
    if(descriptor!=IntPtr.Zero)Native.LocalFree(descriptor);if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);
   }
  }
  public uint Wait(uint milliseconds){if(disposed)throw new ObjectDisposedException(nameof(RestrictedProcess));return Native.WaitForSingleObject(process,milliseconds);}
  public uint ExitCode{get{uint value;PrivateStation.Check(Native.GetExitCodeProcess(process,out value));return value;}}
  public void Kill(uint reason){if(job!=IntPtr.Zero)PrivateStation.Check(Native.TerminateJobObject(job,reason));}
  public void Dispose(){
   if(disposed)return;
   if(job!=IntPtr.Zero){PrivateStation.Check(Native.TerminateJobObject(job,125));}
   if(process!=IntPtr.Zero){uint wait=Native.WaitForSingleObject(process,5000);if(wait!=0)throw new InvalidOperationException("restricted-process-cleanup-kernel-unproven:"+wait);PrivateStation.Check(Native.CloseHandle(process));process=IntPtr.Zero;station.ChildReleased();}
   if(job!=IntPtr.Zero){PrivateStation.Check(Native.CloseHandle(job));job=IntPtr.Zero;}
   if(desktop!=IntPtr.Zero){PrivateStation.Check(Native.CloseDesktop(desktop));desktop=IntPtr.Zero;}
   if(profile!=IntPtr.Zero){Hr(Native.DeleteAppContainerProfile(profileName));Native.FreeSid(profile);profile=IntPtr.Zero;}
   disposed=true;
  }
 }
}

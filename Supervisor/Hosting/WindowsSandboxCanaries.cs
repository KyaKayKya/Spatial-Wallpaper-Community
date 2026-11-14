using System;using System.IO;using System.Security.AccessControl;using System.Security.Principal;
namespace SpatialWallpaper.Supervision.Windows {
 internal static class WindowsSandboxCanaries {
 internal static string Create(string attempts,OwnedAttempt workspace){
   string boundary=workspace.Boundary;Directory.CreateDirectory(boundary);var caller=WindowsIdentity.GetCurrent().User;var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
   foreach(string directory in new[]{attempts,Path.Combine(attempts,"boundary"),boundary}){
    var acl=Directory.GetAccessControl(directory);
    foreach(string package in new[]{"S-1-15-2-1","S-1-15-2-2"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(package),FileSystemRights.Traverse|FileSystemRights.ReadAttributes|FileSystemRights.ReadExtendedAttributes,AccessControlType.Allow));
    Directory.SetAccessControl(directory,acl);
   }
   foreach(string entry in new[]{"aap","arap"}){
    string path=Path.Combine(boundary,entry);using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.WriteByte(83);
    var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new FileSystemAccessRule(caller,FileSystemRights.FullControl,AccessControlType.Allow));acl.AddAccessRule(new FileSystemAccessRule(system,FileSystemRights.FullControl,AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(entry=="aap"?"S-1-15-2-1":"S-1-15-2-2"),FileSystemRights.Read,AccessControlType.Allow));File.SetAccessControl(path,acl);
   }
return boundary;
}
}
}

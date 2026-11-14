using System;using System.IO;using System.Linq;using System.Security.Cryptography;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Hosting {
 public static class HostExitContract {
  public const int Version=1,Success=0,Rejected=1,Forced=2,CleanupFailed=3;
  public static bool ProvesCleanup(int code)=>code==Success||code==Rejected;
  public static void Require(string executable){executable=Path.GetFullPath(executable);var metadata=TrustedRuntimeDistribution.Read(Path.Combine(Path.GetDirectoryName(executable),"build.json"),2*1024*1024);if(metadata["host_cleanup_protocol"]?.Type!=JTokenType.Integer||(int)metadata["host_cleanup_protocol"]!=Version)throw new InvalidDataException("host-cleanup-contract-version");var expected=metadata["files"]?[Path.GetFileName(executable)];if(expected?.Type!=JTokenType.String||((string)expected).Length!=64||((string)expected).Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new InvalidDataException("host-cleanup-image-digest");if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("host-cleanup-image-link");using(var input=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=(string)expected)throw new InvalidDataException("host-cleanup-image-drift");}
 }
}

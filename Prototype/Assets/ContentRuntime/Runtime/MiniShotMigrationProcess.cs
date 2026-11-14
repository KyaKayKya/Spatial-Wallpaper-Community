using System;using System.IO;using System.Diagnostics;using System.ComponentModel;
namespace SpatialWallpaper.Content {
 // Only the manager-created, childless migration helper is accepted by this owner boundary.
 public static class MiniShotMigrationProcess {
  public static void Wait(Process owned,int deadlineMilliseconds=10000,int terminationMilliseconds=2000){
   if(owned==null||deadlineMilliseconds<1||terminationMilliseconds<1)throw new ArgumentException("migration-process-deadline");
   if(owned.WaitForExit(deadlineMilliseconds))return;
   try{if(!owned.HasExited)owned.Kill();}
   catch(InvalidOperationException){if(!owned.HasExited)throw;}
   catch(Win32Exception failure){if(!owned.HasExited)throw new IOException("迁移 helper 终止未证实，可能仍在提交；旧存档保留，请核对目标迁移标记或 receipt。",failure);}
   if(!owned.WaitForExit(terminationMilliseconds)||!owned.HasExited)throw new IOException("迁移 helper 终止未证实，可能仍在提交；旧存档保留，请核对目标迁移标记或 receipt。");
   // CAS may precede the deadline. Never delete/reset the target to simulate rollback.
   throw new TimeoutException("迁移 helper 已终止，可能已提交；旧存档保留，请核对目标迁移标记或 receipt。");
  }
 }
}

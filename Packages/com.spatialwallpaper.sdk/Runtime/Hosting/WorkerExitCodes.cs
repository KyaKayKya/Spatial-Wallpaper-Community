namespace SpatialWallpaper.Sdk.Hosting {
 // Protocol values reported by the platform Worker bootstrap, never proof of the native root cause.
 public static class WorkerExitCodes {
  public const int Admission=30,Script=31,ExecutionBudget=32,ResourceBudget=33,Render=34;
  public static string ReportedReason(uint? code){switch(code){case Admission:return "admission-rejected";case Script:return "script-exception";case ExecutionBudget:return "execution-budget";case ResourceBudget:return "resource-budget";case Render:return "render-transport";default:return "unknown";}}
 }
}

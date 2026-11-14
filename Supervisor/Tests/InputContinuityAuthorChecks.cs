using System;using System.Collections.Generic;using System.Reflection;
namespace UnityEngine {public class MonoBehaviour{}}
namespace SpatialWallpaper.Sdk {
 [AttributeUsage(AttributeTargets.Class)]public sealed class WallpaperScriptAttribute:Attribute{}
 public static class Input {
  public static int Held,Down,Up;public static bool Focus=true,Unstable;static int reads;
  public static void Frame(int held,int down=0,int up=0){Held=held;Down=down;Up=up;reads=0;}
  static int Bit(string n)=>1<<Array.IndexOf(new[]{"Space","Left","Right","Up","Down","Primary","Secondary"},n);
  public static bool GetButton(string n)=>(Held&Bit(n))!=0;
  public static bool GetButtonDown(string n)=>((Down&Bit(n))!=0)^(Unstable&&++reads%2==0);
  public static bool GetButtonUp(string n)=>(Up&Bit(n))!=0;
  public static bool IsFocused()=>Focus;public static float GetPointerX()=>.25f;public static float GetPointerY()=>.75f;public static float GetWheel()=>0;
 }
 public static class Storage {
  public class R {public int State=1;public bool Write,Released,Committed;public string Value,Version;}
  public static Dictionary<int,R> Requests=new Dictionary<int,R>();public static int Id,Revision;public static string Value;
  public static void Reset(){Requests.Clear();Id=Revision=0;Value=null;Input.Focus=true;Input.Unstable=false;}
  public static int BeginRead(string key){Requests[++Id]=new R{Version=Revision.ToString()};return Id;}
  public static int BeginWriteText(string key,string value,string version){Requests[++Id]=new R{Write=true,Value=value,Version=version};return Id;}
  public static int State(int id)=>Requests[id].State;public static string Version(int id)=>Requests[id].Version;
  public static void Release(int id){if(Requests[id].Released)throw new Exception("double-release");Requests[id].Released=true;}
  public static void Commit(int id){var r=Requests[id];if(!r.Write||r.Committed||r.Version!=Revision.ToString())throw new Exception("invalid-commit");r.Committed=true;Revision++;Value=r.Value;}
  public static void Reply(int id){var r=Requests[id];if(r.Write&&!r.Committed)throw new Exception("uncommitted-reply");r.State=3;}
 }
}
class InputContinuityAuthorChecks {
 static MethodInfo update=typeof(StageEInputContinuityAuthor).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
 static void Tick(StageEInputContinuityAuthor a,int held,int down=0,int up=0){SpatialWallpaper.Sdk.Input.Frame(held,down,up);update.Invoke(a,null);}
 static void Need(bool v,string n){if(!v)throw new Exception(n);Console.WriteLine("PASS "+n);}
 static StageEInputContinuityAuthor New(){SpatialWallpaper.Sdk.Storage.Reset();return new StageEInputContinuityAuthor();}
 static void Drain(StageEInputContinuityAuthor a,int held){for(int n=0;n<8&&(a.request!=0||a.queued!=null);n++){if(a.request!=0){var q=SpatialWallpaper.Sdk.Storage.Requests[a.request];if(q.Write&&!q.Committed)SpatialWallpaper.Sdk.Storage.Commit(a.request);SpatialWallpaper.Sdk.Storage.Reply(a.request);}Tick(a,held);}}
 static int Main(){try{
  var a=New();Tick(a,0);Need(a.armed,"focused-author-arms");for(int cycle=0;cycle<3;cycle++)for(int s=0;s<14;s++){int b=1<<(s/2);Tick(a,s%2==0?b:0,s%2==0?b:0,s%2==1?b:0);Drain(a,s%2==0?b:0);Need(a.failure==null&&SpatialWallpaper.Sdk.Storage.Revision==cycle*14+s+1,"ordered-durable-edge-"+cycle+"-"+s);}Need(a.cycles==3&&a.edges==42&&a.heldMask==0,"complete-cycles-count-exact");
  foreach(var q in SpatialWallpaper.Sdk.Storage.Requests.Values)Need(q.Released,"request-released");
  a=New();Tick(a,1,1);SpatialWallpaper.Sdk.Storage.Reply(a.request);Tick(a,1);SpatialWallpaper.Sdk.Storage.Commit(a.request);int delayed=a.request;Tick(a,0,0,1);Need(a.failure==null&&a.queued=="Space-up","commit-before-reply-next-edge-retained");SpatialWallpaper.Sdk.Storage.Reply(delayed);Tick(a,0);Drain(a,0);Need(a.failure==null&&SpatialWallpaper.Sdk.Storage.Value=="Space-up"&&SpatialWallpaper.Sdk.Storage.Revision==2,"late-reply-preserves-next-ack");
  a=New();Tick(a,1,1);Tick(a,0,0,1);Tick(a,2,2);Need(a.failure=="edge-queue-overflow","second-pending-edge-rejected");
  a=New();Tick(a,1,1);Tick(a,1,1);Need(a.failure=="unexpected-edge","duplicate-edge-rejected");
  a=New();Tick(a,2,2);Need(a.failure=="unexpected-edge","out-of-order-edge-rejected");
  a=New();Tick(a,1);Need(a.failure=="missing-edge","held-without-down-rejected");
  a=New();Tick(a,1,1,1);Need(a.failure=="unexpected-edge","coalesced-down-up-rejected");
  a=New();Tick(a,0);SpatialWallpaper.Sdk.Input.Focus=false;Tick(a,0);SpatialWallpaper.Sdk.Input.Focus=true;Tick(a,1,1);Need(a.failure=="focus-lost"&&a.edges==0,"lost-focus-never-recovers");
  a=New();SpatialWallpaper.Sdk.Input.Unstable=true;Tick(a,1,1);Need(a.failure=="unstable-buttons","unstable-frame-rejected");
  foreach(int state in new[]{4,5}){a=New();Tick(a,1,1);SpatialWallpaper.Sdk.Storage.Requests[a.request].State=state;Tick(a,1);Need(a.failure=="storage-failed"&&a.request==0,"terminal-storage-failure-"+state);}
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

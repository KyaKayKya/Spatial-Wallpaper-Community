using System;using System.Reflection;
namespace UnityEngine {public class MonoBehaviour {}}
namespace SpatialWallpaper.Sdk {
 [AttributeUsage(AttributeTargets.Class)]public sealed class WallpaperScriptAttribute:Attribute {}
 public static class Input {
  public static bool Held,Down,Up,Focused,UnstableDown,UnstableWheel;public static float X,Y,Wheel;static int downReads,wheelReads;
  public static void Frame(bool held,bool down,float wheel,bool focused=true,bool up=false,float x=.25f,float y=.75f){Held=held;Down=down;Wheel=wheel;Focused=focused;Up=up;X=x;Y=y;downReads=wheelReads=0;}
  public static bool GetButton(string name)=>Held;
  public static bool GetButtonDown(string name)=>Down&&(!UnstableDown||downReads++==0);
  public static bool GetButtonUp(string name)=>Up;
  public static bool IsFocused()=>Focused;
  public static float GetPointerX()=>X;public static float GetPointerY()=>Y;
  public static float GetWheel()=>Wheel+(UnstableWheel&&wheelReads++>0?1:0);
 }
 public static class Storage {
  public sealed class Request {public string Key,Value,Version;public int Status;public bool Write,Committed,Released;}
  public static readonly System.Collections.Generic.Dictionary<int,Request> Requests=new System.Collections.Generic.Dictionary<int,Request>();
  public static readonly System.Collections.Generic.Dictionary<string,string> Durable=new System.Collections.Generic.Dictionary<string,string>();
  public static bool Controlled;public static int Revision;static int next;
  public static void Reset(){Requests.Clear();Durable.Clear();Revision=next=0;Controlled=true;}
  public static int BeginRead(string key){int id=++next;Requests.Add(id,new Request{Key=key,Status=Controlled?1:3,Version=Revision.ToString()});return id;}
  public static int State(int request)=>Requests[request].Status;
  public static string Version(int request)=>Requests[request].Version;
  public static void Release(int request){if(Requests[request].Released)throw new Exception("double-request-release");Requests[request].Released=true;}
  public static int BeginWriteText(string key,string value,string version){int id=++next;Requests.Add(id,new Request{Key=key,Value=value,Version=version,Write=true,Status=Controlled?1:3});return id;}
  public static void Commit(int id){var request=Requests[id];if(!request.Write||request.Committed||request.Version!=Revision.ToString())throw new Exception("invalid-controlled-commit");request.Committed=true;Durable.Add(request.Key,request.Value);Revision++;}
  public static void Reply(int id){var request=Requests[id];if(request.Write&&!request.Committed)throw new Exception("reply-before-commit");request.Status=3;}
 }
}
static class InputAuthorFixtureChecks {
 static int failed;static readonly MethodInfo update=typeof(StageEInputRuntimeAuthor).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
 static void Tick(StageEInputRuntimeAuthor author,bool held,bool down,float wheel,bool focused=true,bool up=false,float x=.25f,float y=.75f){SpatialWallpaper.Sdk.Input.Frame(held,down,wheel,focused,up,x,y);update.Invoke(author,null);}
 static void Need(bool condition,string name){Console.WriteLine((condition?"PASS ":"FAIL ")+name);if(!condition)failed++;}
 static void DelayedReplies(){
  for(int releasePhase=1;releasePhase<=3;releasePhase++)for(int hidePhase=4;hidePhase<=6;hidePhase++){
   SpatialWallpaper.Sdk.Storage.Reset();var author=new StageEInputRuntimeAuthor();Tick(author,true,true,1);bool released=false,hidden=false;int pending=0,lastRequest=0;
   for(int frame=0;frame<100&&author.phase!=9;frame++){
    bool up=!released&&author.phase==releasePhase;if(up)released=true;if(author.phase==hidePhase)hidden=true;
    if(author.phase==1||author.phase==2||author.phase==4||author.phase==5||author.phase==7||author.phase==8){
     if(lastRequest!=author.request){lastRequest=author.request;pending=0;}var request=SpatialWallpaper.Sdk.Storage.Requests[author.request];
     if(pending==0&&request.Write)SpatialWallpaper.Sdk.Storage.Commit(author.request);
     if(pending++==3)SpatialWallpaper.Sdk.Storage.Reply(author.request);
    }
    Tick(author,!released,false,0,!hidden,up,hidden?0:.25f,hidden?0:.75f);
   }
   Need(author.phase==9&&SpatialWallpaper.Sdk.Storage.Revision==3&&SpatialWallpaper.Sdk.Storage.Durable.Count==3&&SpatialWallpaper.Sdk.Storage.Durable["input-cleared"]=="observed","delayed-commit-reply-release-"+releasePhase+"-hide-"+hidePhase);
   bool allReleased=true;foreach(var request in SpatialWallpaper.Sdk.Storage.Requests.Values)allReleased&=request.Released;Need(allReleased,"all-six-requests-released-"+releasePhase+"-"+hidePhase);
  }
  foreach(int failureState in new[]{1,4,5}){SpatialWallpaper.Sdk.Storage.Reset();var author=new StageEInputRuntimeAuthor();Tick(author,true,true,1);SpatialWallpaper.Sdk.Storage.Requests[author.request].Status=failureState;for(int i=0;i<8;i++)Tick(author,false,false,0,true,i==0);Need(author.phase==1&&author.sawRelease&&SpatialWallpaper.Sdk.Storage.Revision==0,"pending-error-closed-never-fabricates-acceptance-"+failureState);}
  SpatialWallpaper.Sdk.Storage.Controlled=false;
 }
 static int Main(){
  var together=new StageEInputRuntimeAuthor();Tick(together,true,true,1);Need(together.phase==1&&together.sideEffects==0,"same-frame-input-and-short-circuit");
  var split=new StageEInputRuntimeAuthor();Tick(split,true,true,0);Need(split.phase==0,"press-alone-does-not-complete");Tick(split,true,false,1);Need(split.phase==1&&split.sideEffects==0,"legitimate-adjacent-frame-input-completes");
  var missingPress=new StageEInputRuntimeAuthor();Tick(missingPress,true,false,1);Tick(missingPress,true,false,0);Need(missingPress.phase==0,"wheel-without-press-rejected");
  var missingWheel=new StageEInputRuntimeAuthor();Tick(missingWheel,true,true,0);Tick(missingWheel,true,false,0);Need(missingWheel.phase==0,"press-without-wheel-rejected");
  var lostFocus=new StageEInputRuntimeAuthor();Tick(lostFocus,true,true,0);Tick(lostFocus,false,false,0,false);Tick(lostFocus,true,false,1);Need(lostFocus.phase==0,"focus-loss-does-not-reuse-old-press");
  var released=new StageEInputRuntimeAuthor();Tick(released,true,true,0);Tick(released,false,false,0,true,true);Tick(released,true,false,1);Need(released.phase==0,"release-does-not-reuse-old-press");
  var oldRelease=new StageEInputRuntimeAuthor();Tick(oldRelease,true,true,0);Tick(oldRelease,false,false,0,true,true);Tick(oldRelease,true,true,1);Tick(oldRelease,true,false,0);Tick(oldRelease,true,false,0);Tick(oldRelease,true,false,0);Need(oldRelease.phase==3,"new-accepted-press-does-not-reuse-previous-release");Tick(oldRelease,false,false,0,true,true);Need(oldRelease.phase==4,"current-accepted-press-release-completes");
  var outside=new StageEInputRuntimeAuthor();Tick(outside,true,true,1,true,false,.5f);Need(outside.phase==0,"out-of-range-input-rejected");
  SpatialWallpaper.Sdk.Input.UnstableDown=true;var unstableDown=new StageEInputRuntimeAuthor();Tick(unstableDown,true,true,1);Need(unstableDown.phase==0,"unstable-down-snapshot-rejected");SpatialWallpaper.Sdk.Input.UnstableDown=false;
  SpatialWallpaper.Sdk.Input.UnstableWheel=true;var unstableWheel=new StageEInputRuntimeAuthor();Tick(unstableWheel,true,true,1);Need(unstableWheel.phase==0,"unstable-wheel-snapshot-rejected");SpatialWallpaper.Sdk.Input.UnstableWheel=false;
  DelayedReplies();return failed==0?0:1;
 }
}

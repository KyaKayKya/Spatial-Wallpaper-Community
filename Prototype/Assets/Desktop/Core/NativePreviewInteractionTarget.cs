using System;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Desktop {
 public sealed class NativePreviewInteractionTarget:IPreviewInputTarget {
  readonly Func<IPreviewInputTarget> author;
  bool dragging;float previousX,previousY;long sequence,time;
  public double DragX{get;private set;}
  public double DragY{get;private set;}
  public long Escapes{get;private set;}
  public bool Dragging=>dragging;
  public string AuthorError{get;private set;}
  public ulong CaptureEpoch{get{if(AuthorError!=null)return 0;try{return author()?.CaptureEpoch??0;}catch(Exception failure){FailAuthor(failure);return 0;}}}
  public NativePreviewInteractionTarget(Func<IPreviewInputTarget> currentAuthor){author=currentAuthor??throw new ArgumentNullException(nameof(currentAuthor));}
  void FailAuthor(Exception failure){if(AuthorError==null)AuthorError=failure.GetType().Name+":"+failure.Message;try{author()?.Revoke();}catch{}}
  public void Revoke(){dragging=false;if(AuthorError!=null)return;try{author()?.Revoke();}catch(Exception failure){FailAuthor(failure);}}
  public void Escape(){Escapes=checked(Escapes+1);}
  public void Observe(InputObservation value){
   ObserveUi(value);if(AuthorError!=null)return;try{author()?.Observe(value);}catch(Exception failure){FailAuthor(failure);}
  }
  void ObserveUi(InputObservation value){
   if(value.Sequence<=sequence||value.Time<time||!value.Target||!value.Foreground||!value.ShellFocus||!value.Session||!value.Adapter||value.Modifier||float.IsNaN(value.X)||float.IsNaN(value.Y)||value.X<0||value.X>1||value.Y<0||value.Y>1){dragging=false;return;}
   sequence=value.Sequence;time=value.Time;
   if((value.Held&1)==0){dragging=false;return;}
   if(!dragging){if(!value.PrimaryDown||value.Heartbeat)return;dragging=true;}
   else{DragX+=(double)value.X-previousX;DragY-=(double)value.Y-previousY;}
   previousX=value.X;previousY=value.Y;
  }
 }
}

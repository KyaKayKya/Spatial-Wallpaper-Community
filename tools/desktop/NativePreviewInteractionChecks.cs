using System;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Capabilities;

static class NativePreviewInteractionChecks {
 sealed class Sink:IPreviewInputTarget {
  public ulong CaptureEpoch=>17;public int Count,Revoked;public InputObservation Last;
  public void Observe(InputObservation value){Count++;Last=value;}
  public void Revoke(){Revoked++;}
 }
 static int checks;
 static void Need(bool value,string why){if(!value)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
 static InputObservation Sample(long sequence,bool down,uint held,float x,float y,bool modifier=false,bool heartbeat=false)=>new InputObservation(sequence,1234+sequence,true,true,true,true,true,modifier,down,held,x,y,0,17,heartbeat);
 static int Main(){try{return Run();}catch(Exception failure){Console.Error.WriteLine(failure.GetType().Name+":"+failure.Message);return 1;}}
 static int Run(){
  var sink=new Sink();IPreviewInputTarget author=sink;var target=new NativePreviewInteractionTarget(()=>author);
  Need(target.CaptureEpoch==17,"author-original-capture-epoch");target.Observe(Sample(1,true,1,.2f,.3f));target.Observe(Sample(2,false,1,.4f,.2f));Need(Math.Abs(target.DragX-.2)<.00001&&Math.Abs(target.DragY-.1)<.00001,"trusted-normalized-drag-cumulative");Need(sink.Count==2&&sink.Last.Sequence==2&&sink.Last.Time==1236&&sink.Last.CaptureEpoch==17,"author-original-time-order-epoch-preserved");
  target.Revoke();Need(sink.Revoked==1,"source-revoke-forwarded-to-author");double x=target.DragX,y=target.DragY;target.Observe(Sample(3,false,1,.8f,.8f));Need(target.DragX==x&&target.DragY==y,"held-on-reentry-does-not-start-drag");
  target.Observe(Sample(4,true,1,.3f,.4f));target.Observe(Sample(5,false,1,.5f,.6f,true));target.Observe(Sample(6,false,1,.8f,.9f));Need(target.DragX==x&&target.DragY==y,"modifier-revokes-trusted-drag");
  author=null;Need(target.CaptureEpoch==0,"no-author-no-capability-epoch");target.Observe(Sample(7,true,1,.2f,.2f));target.Observe(Sample(8,false,1,.3f,.3f));Need(target.DragX>x&&target.DragY<y,"ui-remains-live-without-author");int count=sink.Count;target.Escape();Need(target.Escapes==1&&sink.Count==count,"trusted-escape-does-not-forge-author-event");
  target.Observe(Sample(9,false,0,.3f,.3f));x=target.DragX;y=target.DragY;target.Observe(Sample(10,false,1,.9f,.9f,true,true));Need(target.DragX==x&&target.DragY==y,"heartbeat-cannot-start-drag");
  target.Observe(Sample(11,true,1,.2f,.2f));target.Observe(Sample(10,false,1,.7f,.7f));Need(target.DragX==x&&target.DragY==y,"regressed-sequence-revokes-drag");
  target.Observe(Sample(12,true,1,.2f,.2f));target.Observe(Sample(13,false,1,float.NaN,.7f));Need(!double.IsNaN(target.DragX)&&target.DragX==x,"nonfinite-pointer-cannot-corrupt-ui");
  using(var closed=new AsyncPreviewInputTarget(()=>sink)){
   closed.Dispose();var isolated=new NativePreviewInteractionTarget(()=>closed);isolated.Observe(Sample(1,true,1,.2f,.2f));isolated.Observe(Sample(2,false,1,.4f,.4f));isolated.Escape();Need(isolated.DragX>.19&&isolated.Escapes==1,"closed-author-does-not-kill-trusted-UI");Need(isolated.GetType().GetProperty("AuthorError")?.GetValue(isolated)!=null&&isolated.CaptureEpoch==0,"closed-author-error-explicit-and-no-author-authority");isolated.Revoke();Need(isolated.Escapes==1,"closed-author-revocation-does-not-escape");closed.Completion.GetAwaiter().GetResult();
  }
  Console.WriteLine("CHECKS "+checks);return 0;
 }
}

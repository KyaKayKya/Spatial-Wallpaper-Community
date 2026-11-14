using System;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Hosting;
static class FrameMailboxChecks {
 static void Assert(bool yes,string message){if(!yes)throw new Exception(message);}
 internal static void Run(){
  RuntimeLimits.RequireRenderSize(1920,1080);RuntimeLimits.RequireRenderSize(1080,1920);
  foreach(var size in new[]{new[]{3840,2160},new[]{0,1080},new[]{1920,1920}}){bool denied=false;try{RuntimeLimits.RequireRenderSize(size[0],size[1]);}catch(PlatformNotSupportedException){denied=true;}Assert(denied,"unvalidated-render-size-accepted");}
  using(var ledger=new ResourceLedger(new ResourceCost(4*1024*1024,65536,0,16))){
   var queue=new FrameMailbox(ledger,640,360);
   var first=queue.AcquireCapture();first.Pixels[0]=1;queue.Publish(first,1);
   var sending=queue.Take();Assert(sending==first,"first-frame-not-taken");
   var second=queue.AcquireCapture();second.Pixels[0]=2;queue.Publish(second,2);
   var third=queue.AcquireCapture();third.Pixels[0]=3;queue.Publish(third,3);
   Assert(first.Pixels[0]==1,"in-flight-pixel-buffer-overwritten");
   queue.Sent(sending);var latest=queue.Take();Assert(latest==third&&latest.Sequence==3&&latest.Pixels[0]==3,"stale-pending-frame-not-replaced");
   queue.Sent(latest);Assert(queue.LastSent==3&&queue.Dropped==1,"frame-statistics");
   var abandoned=queue.AcquireCapture();queue.CancelCapture(abandoned);
   queue.Close();Assert(queue.Take()==null,"closed-mailbox-blocked");Assert(queue.AcquireCapture()==null,"closed-mailbox-captured");
  }
  using(var ledger=new ResourceLedger(new ResourceCost(8192+2*(1920*1080*4+128),65536,0,16))){
   var queue=new FrameMailbox(ledger,1920,1080,2);var one=queue.AcquireLatestCapture();one.Pixels[0]=1;queue.Publish(one,1);var sending=queue.Take();
   var two=queue.AcquireLatestCapture();two.Pixels[0]=2;queue.Publish(two,2);var replacement=queue.AcquireLatestCapture();Assert(replacement==two&&one.Pixels[0]==1,"two-slot-latest-capture-overwrote-sending");replacement.Pixels[0]=3;queue.Publish(replacement,3);
   queue.Sent(sending);var latest=queue.Take();Assert(latest.Sequence==3&&latest.Pixels[0]==3&&queue.Dropped==1,"two-slot-latest-frame-or-drop-count");queue.Sent(latest);queue.Close();Assert(queue.AcquireLatestCapture()==null,"two-slot-close");
  }
  using(var ledger=new ResourceLedger(new ResourceCost(8192+1920*1080*4+128,65536,0,16))){
   var queue=new FrameMailbox(ledger,1920,1080,1);var first=queue.AcquireLatestCapture();first.Pixels[0]=7;queue.Publish(first,1);var sending=queue.Take();
   Assert(queue.AcquireLatestCapture()==null&&sending.Pixels[0]==7,"single-slot-mutated-inflight-frame");queue.Sent(sending);
   var next=queue.AcquireLatestCapture();Assert(next==first,"single-slot-not-reused-after-send");next.Pixels[0]=9;queue.Publish(next,2);var replacement=queue.AcquireLatestCapture();Assert(replacement==next,"single-slot-pending-not-replaceable");queue.Publish(replacement,3);var last=queue.Take();Assert(last.Sequence==3&&queue.Dropped==1,"single-slot-latest-order");queue.Close();queue.Sent(last);Assert(queue.Take()==null&&queue.AcquireLatestCapture()==null,"single-slot-stop");
  }
  using(var ledger=new ResourceLedger(new ResourceCost(1024,65536,0,16))){
   bool denied=false;try{new FrameMailbox(ledger,640,360);}catch(ResourceBudgetException){denied=true;}Assert(denied&&ledger.Used.ManagedBytes==0,"frame-buffer-budget-not-preflighted");
  }
  Console.WriteLine("PASS bounded capture/pending/sending buffers, replacement, stop and preflight");
 }
}

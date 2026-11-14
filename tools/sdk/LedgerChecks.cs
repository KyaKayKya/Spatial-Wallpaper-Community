using System;using System.IO;using System.Threading.Tasks;using SpatialWallpaper.Sdk.Execution;
class LedgerChecks {
 static void Assert(bool yes,string message){if(!yes)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ResourceBudgetException){return;}throw new Exception("allocation-not-rejected");}
 static int Main(){
  try{
   using(var ledger=new ResourceLedger(new ResourceCost(100,200,300,4))){
    using(var pending=ledger.Reserve(new ResourceCost(50,100,150,2))){Assert(ledger.Used.ManagedBytes==50&&ledger.Reserved.Objects==2,"reservation-not-charged");}
    Assert(ledger.Used.Objects==0,"failed-creation-not-refunded");
    var live=ledger.Reserve(new ResourceCost(50,100,150,2));live.Commit();Assert(ledger.Committed.Objects==2&&ledger.Reserved.Objects==0,"commit-accounting");
    Reject(()=>ledger.Reserve(new ResourceCost(51,0,0,0)));Reject(()=>ledger.Reserve(new ResourceCost(0,101,0,0)));Reject(()=>ledger.Reserve(new ResourceCost(0,0,151,0)));Reject(()=>ledger.Reserve(new ResourceCost(0,0,0,3)));
    Assert(ledger.Used.ManagedBytes==50&&ledger.Used.NativeBytes==100&&ledger.Used.GpuBytes==150&&ledger.Used.Objects==2,"failed-reserve-mutated-ledger");
    live.Grow(new ResourceCost(10,20,30,1));Assert(ledger.Used.ManagedBytes==60&&ledger.Committed.Objects==3,"committed-growth-not-charged");
    Reject(()=>live.Grow(new ResourceCost(41,0,0,0)));Assert(live.Cost.ManagedBytes==60,"failed-growth-mutated-lease");
    live.Dispose();live.Dispose();Assert(ledger.Used.Objects==0&&ledger.Used.ManagedBytes==0,"duplicate-refund");
   }
   using(var ledger=new ResourceLedger(new ResourceCost(long.MaxValue,0,0,0))){
    using(var maximum=ledger.Reserve(new ResourceCost(long.MaxValue,0,0,0))){Reject(()=>ledger.Reserve(new ResourceCost(1,0,0,0)));Assert(ledger.Used.ManagedBytes==long.MaxValue,"overflow-corrupted-ledger");}
   }
   using(var ledger=new ResourceLedger(new ResourceCost(100,0,0,100))){
    Parallel.For(0,1000,i=>{if(ledger.TryReserve(new ResourceCost(1,0,0,1),out var lease)){lease.Commit();lease.Dispose();}});
    Assert(ledger.Used.Objects==0&&ledger.Peak.Objects<=100&&ledger.Peak.ManagedBytes<=100,"concurrent-oversubscription");
    var late=ledger.Reserve(new ResourceCost(10,0,0,1));ledger.Dispose();late.Dispose();Assert(ledger.Used.Objects==0,"late-refund-after-stop");
   }
   var texture=ResourceEstimates.Texture(64,32,4,1,1);
   var admittedTexture=new byte[44+1024*1024];using(var stream=new MemoryStream(admittedTexture,true))using(var writer=new BinaryWriter(stream)){writer.Write(0x32545753u);foreach(int field in new[]{1024,1024,24,1,0,1,1,1,8})writer.Write(field);writer.Write(0f);}
   var copiedTexture=ResourceEstimates.EncodedTexture(admittedTexture);var borrowedTexture=ResourceEstimates.BorrowedTexture(admittedTexture);
   Assert(borrowedTexture.ManagedBytes==4096&&borrowedTexture.NativeBytes==copiedTexture.NativeBytes&&borrowedTexture.GpuBytes==copiedTexture.GpuBytes&&borrowedTexture.Objects==copiedTexture.Objects,"borrowed-import-keeps-full-native-and-GPU-estimates");
   using(var ledger=new ResourceLedger(new ResourceCost(admittedTexture.Length+borrowedTexture.ManagedBytes,borrowedTexture.NativeBytes,borrowedTexture.GpuBytes,1))){using(var snapshot=ledger.Reserve(new ResourceCost(admittedTexture.Length,0,0,0)))using(var imported=ledger.Reserve(borrowedTexture)){Reject(()=>ledger.Reserve(new ResourceCost(1,0,0,0)));Assert(ledger.Used.ManagedBytes==admittedTexture.Length+4096,"immutable-bytes-and-wrapper-both-charged");}}
   Assert(texture.Objects==1&&texture.GpuBytes>=64*32*4&&texture.NativeBytes>=64*32*4,"texture-estimate-underflow");
   var cube=ResourceEstimates.Texture(32,32,17,6,6);
   Assert(cube.GpuBytes>32*32*8*6,"cubemap-mips-not-charged");
   try{ResourceEstimates.Texture(int.MaxValue,32,4,1,1);throw new Exception("dimensions-not-rejected");}catch(ArgumentOutOfRangeException){}
   try{ResourceEstimates.Mesh(-1,3);throw new Exception("negative-mesh-not-rejected");}catch(ArgumentOutOfRangeException){}
   var compactShader=ResourceEstimates.ShaderBundle(1024,4096);var expandedShader=ResourceEstimates.ShaderBundle(1024,4*1024*1024);
   Assert(expandedShader.NativeBytes>compactShader.NativeBytes*100&&expandedShader.GpuBytes>compactShader.GpuBytes*100,"shader-decoding-amplification-not-charged");
   try{ResourceEstimates.ShaderBundle(1024,3L*33554432+1);throw new Exception("invalid-shader-decoded-budget");}catch(ArgumentOutOfRangeException){}
   var shadowEstimator=typeof(ResourceEstimates).GetMethod("ShadowAtlases");
   Assert(shadowEstimator!=null,"fixed-shadow-atlas-estimator-missing");
   var shadows=(ResourceCost)shadowEstimator.Invoke(null,new object[]{2048,2048});
   Assert(shadows.Objects==2&&shadows.GpuBytes>=2L*2048*2048*4,"fixed-shadow-atlases-not-charged");
   using(var ledger=new ResourceLedger(new ResourceCost(long.MaxValue,long.MaxValue,shadows.GpuBytes-1,16))){Reject(()=>ledger.Reserve(shadows));Assert(ledger.Used.GpuBytes==0,"shadow-atlas-rejection-mutated-ledger");}
   var post=ResourceEstimates.PostProcessing(1920,1080,32,true);var lut=ResourceEstimates.PostProcessing(1920,1080,32,false);
   Assert(post.GpuBytes>=1920L*1080*32&&post.GpuBytes>lut.GpuBytes&&post.Objects>lut.Objects,"post-processing-targets-not-charged");
   using(var ledger=new ResourceLedger(new ResourceCost(long.MaxValue,long.MaxValue,post.GpuBytes-1,64))){Reject(()=>ledger.Reserve(post));Assert(ledger.Used.GpuBytes==0,"post-rejection-mutated-ledger");}
   try{ResourceEstimates.PostProcessing(1920,1080,0,true);throw new Exception("invalid-lut-not-rejected");}catch(ArgumentOutOfRangeException){}
   Console.WriteLine("PASS atomic managed/native/GPU/object reservations, overflow, concurrency and rollback");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

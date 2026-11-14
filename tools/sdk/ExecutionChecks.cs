using System;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Security;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;
class ExecutionChecks {
 static void Assert(bool yes,string name){if(!yes)throw new Exception(name);}
 static void Reject(Action action,string name){try{action();}catch(InvalidOperationException){return;}throw new Exception(name);}
 sealed class Host:IVmHost {
  public readonly OwnedDomain Domain;
  public Host(OwnedDomain domain){Domain=domain;}
  public bool Supported;public object Result;public int Resolved,Calls;public Action BeforeReturn;
  public bool Supports(string member)=>Supported;
  public object Resolve(string type,JToken value,int scope){Resolved++;return Domain.Register(type,new object());}
  public object Call(string member,object receiver,object[] arguments){Calls++;BeforeReturn?.Invoke();return Result;}
 }
 static JObject Ins(string op,object value=null)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
 static AdmittedBuild BodyBuild(AdmittedBuild original,JArray body,string localType=null){
  var program=original.Program;program["classes"][0]["methods"][0]["body"]=body;if(localType!=null)program["classes"][0]["methods"][0]["locals"]=new JArray(localType);return Admission.Admit(PackageWriter.Export(program,original.Content,new System.Collections.Generic.Dictionary<string,byte[]>(),(JArray)original.Manifest["licenses"]),"windows-x86_64");
 }
 static AdmittedBuild HostCallBuild(AdmittedBuild original){
  var program=original.Program;
  var method=((JArray)program["classes"][0]["methods"]).Single(m=>(string)m["id"]=="Update");
  method["body"]=new JArray(Ins("call","SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh"),Ins("pop"),Ins("ret"));
  return Admission.Admit(PackageWriter.Export(program,original.Content,new System.Collections.Generic.Dictionary<string,byte[]>(),(JArray)original.Manifest["licenses"]),"windows-x86_64");
 }
 static void BitwiseChecks(AdmittedBuild original){
  foreach(string op in new[]{"and","or","xor"})foreach(bool leftBool in new[]{false,true})foreach(bool rightBool in new[]{false,true})foreach(int left in new[]{0,1,2,-1})foreach(int right in new[]{0,1,2,-1}){
   if(leftBool&&left!=0&&left!=1||rightBool&&right!=0&&right!=1)continue;
   int expected=op=="and"?left&right:op=="or"?left|right:left^right;
   var body=new JArray(Ins("ldc.i4",left));if(leftBool)body.Add(Ins("conv.bool"));body.Add(Ins("ldc.i4",right));if(rightBool)body.Add(Ins("conv.bool"));body.Add(Ins(op));body.Add(Ins("stloc",0));body.Add(Ins("ldloc",0));body.Add(Ins("ldc.i4",expected));if(leftBool&&rightBool)body.Add(Ins("conv.bool"));body.Add(Ins("ceq"));
   var branch=Ins("brfalse",0);body.Add(branch);body.Add(Ins("ldarg",0));body.Add(Ins("ldc.i4",123));body.Add(Ins("stfield","counter"));body.Add(Ins("ret"));branch["value"]=body.Count;body.Add(Ins("ret"));
   using(var owner=new OwnedDomain(Guid.NewGuid(),1)){
    var runtime=new VmRuntime(BodyBuild(original,body,leftBool&&rightBool?"bool":"i32"),owner,new Host(owner),new ExecutionQuota(1000,16,1000));runtime.Start();runtime.Update();Assert((int)runtime.Field(0,"counter")==123,"mixed-bitwise:"+op+":"+leftBool+":"+rightBool+":"+left+":"+right);
   }
  }
  foreach(string op in new[]{"and","or","xor"})foreach(string invalid in new[]{"ldc.r4","ldstr","ldnull"})foreach(bool invalidLeft in new[]{false,true}){
   var bad=Ins(invalid,invalid=="ldc.r4"?(object)1f:invalid=="ldstr"?"x":null);var good=Ins("ldc.i4",1);
   var body=new JArray(invalidLeft?bad:good,invalidLeft?good:bad,Ins(op),Ins("pop"),Ins("ret"));bool denied=false;
   try{BodyBuild(original,body);}catch(InvalidDataException){denied=true;}Assert(denied,"non-integral-bitwise-admitted:"+invalid);
  }
  Console.WriteLine("PASS bool/int bitwise and/or/xor both orders, 0/1/2/-1 and invalid operand rejection");
 }
 static void ValueAccessChecks(AdmittedBuild original){
  foreach(int remaining in new[]{31,32})using(var ledger=new ResourceLedger(new ResourceCost(1048576,0,0,1024)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
   var body=new JArray(Ins("call","SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3"),Ins("getcomponent",0),Ins("pop"),Ins("ldarg",0),Ins("ldc.i4",123),Ins("stfield","counter"),Ins("ret"));
   var host=new Host(owner){Supported=true,Result=new VectorValue("vector3",.1f,.2f,.3f)};var vm=new VmRuntime(BodyBuild(original,body),owner,host,new ExecutionQuota(1000,16,1000));vm.Start();IDisposable fill=null;
   host.BeforeReturn=()=>{fill=ledger.Reserve(new ResourceCost(ledger.Limit.ManagedBytes-ledger.Used.ManagedBytes-remaining,0,0,0));};
   try{bool denied=false;try{vm.Update();}catch(ResourceBudgetException){denied=true;}Assert(host.Calls==1&&denied==(remaining==31)&&(int)vm.Field(0,"counter")== (remaining==31?0:123),"component-boxing-budget:"+remaining);}finally{fill?.Dispose();}
  }
  foreach(int branch in new[]{0,1}){
   var body=new JArray(Ins("ldc.i4",branch),Ins("brtrue",4),Ins("ldstr","hit"),Ins("br",5),Ins("ldnull"),Ins("stloc",0),Ins("ldloc",0),branch==0?Ins("ldstr","hit"):Ins("ldnull"),Ins("ceq"),Ins("brfalse",14),Ins("ldarg",0),Ins("ldc.i4",123),Ins("stfield","counter"),Ins("ret"),Ins("ret"));
   using(var owner=new OwnedDomain(Guid.NewGuid(),1)){var vm=new VmRuntime(BodyBuild(original,body,"string"),owner,new Host(owner),new ExecutionQuota(1000,16,1000));vm.Start();vm.Update();Assert((int)vm.Field(0,"counter")==123,"null-string-stack-join:"+branch);}
  }
  foreach(string type in new[]{"string","node","Demo"}){
   JObject value=type=="string"?Ins("ldstr","saved-hit"):Ins("ldarg",0);var producer=new JArray(value);if(type=="node")producer.Add(Ins("ldfield","target"));
   var body=new JArray(Ins("ldc.i4",1),Ins("newarray",type),Ins("dup"),Ins("ldc.i4",0));foreach(var ins in producer)body.Add(ins.DeepClone());body.Add(Ins("stelem.ref"));body.Add(Ins("ldc.i4",0));body.Add(Ins("ldelem.ref"));foreach(var ins in producer)body.Add(ins.DeepClone());body.Add(Ins("ceq"));
   CompareResult(original,body,true,"reference-array-roundtrip:"+type);
  }
  foreach(int component in new[]{0,1,2}){
   var body=new JArray(Ins("ldc.r4",.125f),Ins("ldc.r4",-.25f),Ins("ldc.r4",.5f),Ins("newvector3"),Ins("getcomponent",component),Ins("ldc.r4",new[]{.125f,-.25f,.5f}[component]),Ins("ceq"));CompareResult(original,body,true,"vector-component:"+component);
  }
  foreach(var body in new[]{
   new JArray(Ins("ldc.i4",1),Ins("newarray","i32"),Ins("ldc.i4",0),Ins("ldelem.ref"),Ins("pop"),Ins("ret")),
   new JArray(Ins("ldc.i4",1),Ins("newarray","string"),Ins("ldc.i4",0),Ins("ldc.r4",1f),Ins("stelem.ref"),Ins("ret")),
   new JArray(Ins("ldc.r4",1f),Ins("getcomponent",0),Ins("pop"),Ins("ret")),
   new JArray(Ins("ldc.r4",1f),Ins("ldc.r4",2f),Ins("ldc.r4",3f),Ins("newvector3"),Ins("getcomponent",3),Ins("pop"),Ins("ret")),
   new JArray(Ins("ret"),Ins("getcomponent",4))
  }){bool denied=false;try{BodyBuild(original,body);}catch(InvalidDataException){denied=true;}Assert(denied,"invalid-reference-array-or-vector-component-admitted");}
  Console.WriteLine("PASS typed reference arrays and vector components with invalid array/store/receiver/index rejection");
 }
 static void CompareResult(AdmittedBuild original,JArray body,bool expected,string name,bool nonFinite=false){
  body.Add(Ins("stloc",0));body.Add(Ins("ldloc",0));body.Add(Ins("ldc.i4",expected?1:0));body.Add(Ins("conv.bool"));body.Add(Ins("ceq"));var branch=Ins("brfalse",0);body.Add(branch);body.Add(Ins("ldarg",0));body.Add(Ins("ldc.i4",123));body.Add(Ins("stfield","counter"));body.Add(Ins("ret"));branch["value"]=body.Count;body.Add(Ins("ret"));
  using(var owner=new OwnedDomain(Guid.NewGuid(),1)){var vm=new VmRuntime(BodyBuild(original,body,"bool"),owner,new Host(owner),new ExecutionQuota(1000,16,1000));vm.Start();if(nonFinite){Reject(()=>vm.Update(),"non-finite-compare-not-rejected");Assert(vm.State==VmState.Faulted,"non-finite-compare-not-faulted");}else{vm.Update();Assert((int)vm.Field(0,"counter")==123,name);}}
 }
 static void FloatConstant(JArray body,float value){
  if(float.IsNaN(value)||float.IsInfinity(value)){body.Add(Ins("ldc.r4",float.IsNaN(value)?0f:value>0?1f:-1f));body.Add(Ins("ldc.r4",0f));body.Add(Ins("div"));}else body.Add(Ins("ldc.r4",value));
 }
 static void ReferenceBranchChecks(AdmittedBuild original){
  var producers=new[]{new JArray(Ins("ldnull")),new JArray(Ins("ldstr","")),new JArray(Ins("ldstr","0")),new JArray(Ins("ldarg",0)),new JArray(Ins("ldarg",0),Ins("ldfield","target")),new JArray(Ins("ldc.i4",0),Ins("newarray","i32")),new JArray(Ins("ldc.i4",0),Ins("newlist","list_i32"))};
  for(int i=0;i<producers.Length;i++){
   foreach(string op in new[]{"brtrue","brfalse"}){
    var body=(JArray)producers[i].DeepClone();var branch=Ins(op,0);body.Add(branch);body.Add(Ins("ldarg",0));body.Add(Ins("ldc.i4",111));body.Add(Ins("stfield","counter"));var end=Ins("br",0);body.Add(end);branch["value"]=body.Count;body.Add(Ins("ldarg",0));body.Add(Ins("ldc.i4",222));body.Add(Ins("stfield","counter"));end["value"]=body.Count;body.Add(Ins("ret"));
    using(var owner=new OwnedDomain(Guid.NewGuid(),1)){var vm=new VmRuntime(BodyBuild(original,body),owner,new Host(owner),new ExecutionQuota(1000,16,1000));vm.Start();vm.Update();Assert((int)vm.Field(0,"counter")==((op=="brtrue")== (i!=0)?222:111),"reference-branch:"+op+":"+i);}
   }
   var comparison=(JArray)producers[i].DeepClone();comparison.Add(Ins("ldnull"));comparison.Add(Ins("cgt.un"));CompareResult(original,comparison,i!=0,"reference-nonnull:"+i);
  }
  Console.WriteLine("PASS null, empty/nonempty string, script, handle, empty array/list branch truth and nonnull comparison");
 }
 static void UnorderedChecks(AdmittedBuild original){
  foreach(string op in new[]{"clt.un","cgt.un","cle.un","cge.un"})foreach(int a in new[]{-1,int.MinValue,0,1,int.MaxValue})foreach(int b in new[]{-1,int.MinValue,0,1,int.MaxValue}){
   uint x=unchecked((uint)a),y=unchecked((uint)b);bool expected=op=="clt.un"?x<y:op=="cgt.un"?x>y:op=="cle.un"?x<=y:x>=y;
   CompareResult(original,new JArray(Ins("ldc.i4",a),Ins("ldc.i4",b),Ins(op)),expected,"unsigned-compare:"+op+":"+a+":"+b);
  }
  foreach(string op in new[]{"clt","cgt","cle","cge","ceq","clt.un","cgt.un","cle.un","cge.un"})foreach(float a in new[]{-1f,0f,-0f,1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})foreach(float b in new[]{-1f,0f,-0f,1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
   bool unordered=float.IsNaN(a)||float.IsNaN(b);bool expected=op=="clt"?a<b:op=="cgt"?a>b:op=="cle"?a<=b:op=="cge"?a>=b:op=="ceq"?a==b:unordered||(op=="clt.un"?a<b:op=="cgt.un"?a>b:op=="cle.un"?a<=b:a>=b);
   // Arithmetic kernel follows CIL semantics; author execution separately rejects
   // non-finite operands at Pop. Do not relax that ownership/value boundary.
   var kernel=typeof(VmRuntime).GetMethod("Binary",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
   Assert((bool)kernel.Invoke(null,new object[]{op,a,b})==expected,"float-kernel:"+op+":"+a+":"+b);
   var body=new JArray();FloatConstant(body,a);FloatConstant(body,b);body.Add(Ins(op));CompareResult(original,body,expected,"float-compare:"+op+":"+a+":"+b,float.IsNaN(a)||float.IsNaN(b)||float.IsInfinity(a)||float.IsInfinity(b));
  }
  foreach(string op in new[]{"clt.un","cgt.un","cle.un","cge.un"})foreach(var body in new[]{new JArray(Ins("ldc.i4",1),Ins("ldc.r4",1f)),new JArray(Ins("ldc.i4",1),Ins("conv.bool"),Ins("ldc.i4",1),Ins("conv.bool")),new JArray(Ins("ldnull"),Ins("ldnull"))}){
   if(op=="cgt.un"&&(string)body[0]["op"]=="ldnull")continue; // CLI nonnull test, covered with reference branches.
   body.Add(Ins(op));body.Add(Ins("pop"));body.Add(Ins("ret"));bool denied=false;try{BodyBuild(original,body);}catch(InvalidDataException){denied=true;}Assert(denied,"unordered-invalid-operands:"+op);
  }
  Console.WriteLine("PASS unsigned integer and finite floating comparisons; kernel unordered semantics; author NaN/infinity fault; exact bool results and invalid operands");
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static WeakReference AbandonArray(OwnedDomain domain){return new WeakReference(domain.Array("i32",new object[]{1,2,3}));}
 static int Main(string[] args){
  try{
   using(var ledger=new ResourceLedger(new ResourceCost(1048576,0,0,1024)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
    long baseline=ledger.Used.ManagedBytes;WeakReference weak=null;var allocator=new System.Threading.Thread(()=>weak=AbandonArray(owner));allocator.Start();allocator.Join();
    Assert(ledger.Used.ManagedBytes>baseline,"owned-array-was-not-charged");
    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
    Console.WriteLine("GC observation alive="+weak.IsAlive+" used="+ledger.Used.ManagedBytes+" baseline="+baseline);Assert(!weak.IsAlive&&ledger.Used.ManagedBytes==baseline,"domain-strong-registry-retained-array");
   }
   using(var ledger=new ResourceLedger(new ResourceCost(1048576,0,0,1024)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
    long checkpoint=owner.Checkpoint();WeakReference weak=null;var allocator=new System.Threading.Thread(()=>weak=AbandonArray(owner));allocator.Start();allocator.Join();
    var gate=typeof(OwnedDomain).GetField("gate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(owner);
    System.Threading.Monitor.Enter(gate);
    try{GC.Collect();owner.Rollback(checkpoint);}finally{System.Threading.Monitor.Exit(gate);}
    GC.WaitForPendingFinalizers();GC.Collect();Assert(!weak.IsAlive,"rollback-held-unreachable-array");
   }
   Console.WriteLine("PASS owned allocation charging and unreachable collection reclamation");
   using(var ledger=new ResourceLedger(new ResourceCost(4096,0,0,4)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
    var boundedList=owner.List("i32",new object[]{1,2,3,4});long before=ledger.Used.ManagedBytes;
    Reject(()=>owner.CreateArray("i32",4096,i=>0),"array-allocation-bypassed-budget");
    // Occupy the remaining allowance so List growth must reject before modifying data.
    using(var fill=ledger.Reserve(new ResourceCost(4096-before,0,0,0))){
     Reject(()=>owner.Add(boundedList,5),"list-growth-bypassed-budget");Assert(boundedList.Count==4&&(int)owner.Get(boundedList,3)==4,"failed-growth-damaged-old-data");
    }
    Assert(ledger.Used.ManagedBytes==before,"failed-array-or-growth-leaked-reservation");
   }
   Console.WriteLine("PASS pre-allocation array/list rejection preserves existing state");
   var a=new OwnedDomain(Guid.NewGuid(),1);var b=new OwnedDomain(Guid.NewGuid(),1);
   var handle=a.Register("node",new object());
   a.Check(handle);Reject(()=>b.Check(handle),"cross-instance");
   var collection=a.Array("node",new object[]{handle});
   a.Check(collection);Reject(()=>b.Check(collection),"array-cross-instance");
   Reject(()=>a.Array("node",new object[]{b.Register("node",new object())}),"nested-foreign-reference");
   var list=a.List("i32",new object[]{1,2});a.Add(list,3);Assert((int)a.Get(list,2)==3,"list-add");a.Set(list,1,9);Assert((int)a.Get(list,1)==9,"list-set");
   Reject(()=>b.Check(list),"list-cross-instance");Reject(()=>a.List("i32",new object[0],4097),"list-capacity");
   a.Clear(list);Assert(list.Count==0,"list-clear");
   a.Invalidate(handle);Reject(()=>a.Check(handle),"destroyed-handle");
   a.Set(collection,0,null);Assert(a.Get(collection,0)==null,"stale-reference-cannot-be-cleared");
   var old=a.Register("mesh",new object());a.Dispose();Reject(()=>a.Check(old),"disposed-domain");
   var replacement=new OwnedDomain(a.Instance,2);Reject(()=>replacement.Check(old),"old-generation");
   Console.WriteLine("PASS ownership, stale references and bounded collections");
   var build=Admission.Admit(File.ReadAllBytes(args[0]),"windows-x86_64");
   ValueAccessChecks(build);
   BitwiseChecks(build);
   ReferenceBranchChecks(build);
   UnorderedChecks(build);
   Assert(build.MethodStack("Demo.Update")==3&&build.SnapshotManagedBytes>0,"verified-program-layout");
   using(var ledger=new ResourceLedger(new ResourceCost(4096,0,0,4)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){var host=new Host(owner);Reject(()=>new VmRuntime(build,owner,host,new ExecutionQuota(1000,16,1000)),"program-copy-was-not-reserved-before-binding");Assert(host.Resolved==0,"budget-denied-build-resolved-fields");}
   using(var ledger=new ResourceLedger(new ResourceCost(1048576,0,0,1024)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
    var runtime=new VmRuntime(build,owner,new Host(owner),new ExecutionQuota(1000,16,1000));runtime.Start();long peak=ledger.Peak.ManagedBytes;
    runtime.Update();Assert(ledger.Peak.ManagedBytes>peak,"VM-temporary-allocations-were-not-charged");
   }
   using(var ledger=new ResourceLedger(new ResourceCost(1048576,0,0,1024)))using(var owner=new OwnedDomain(Guid.NewGuid(),1,ledger)){
    var runtime=new VmRuntime(build,owner,new Host(owner),new ExecutionQuota(1000,16,1000));runtime.Start();
    using(var fill=ledger.Reserve(new ResourceCost(ledger.Limit.ManagedBytes-ledger.Used.ManagedBytes,0,0,0))){
     Reject(()=>runtime.Update(),"frame-allocation-bypassed-budget");Assert(runtime.State==VmState.Faulted&&(int)runtime.Field(0,"counter")==0,"budget-fault-entered-author-callback");
    }
   }
   Console.WriteLine("PASS VM transient allocation reservations before callbacks");
   using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
    var runtime=new VmRuntime(build,domain,new Host(domain),new ExecutionQuota(1000,16,1000));
    runtime.Start();runtime.Update();runtime.Update();
    Assert((int)runtime.Field(0,"counter")==2,"Update-counter");
    Assert(runtime.State==VmState.Running,"runtime-state");
    runtime.Stop();Reject(()=>runtime.Update(),"no-callback-after-stop");
   }
   foreach(float number in new[]{100001f,-100001f,100000f,-100000f}){
    bool outside=Math.Abs(number)>100000;
    var bodies=new[]{
     new JArray(Ins("ldarg",0),Ins("ldfield","target"),Ins("ldc.r4",number),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("newvector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"),Ins("ret")),
     new JArray(Ins("ldnull"),Ins("ldstr","_Color"),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("ldc.r4",number),Ins("newcolor"),Ins("call","UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void"),Ins("ret")),
     new JArray(Ins("ldarg",0),Ins("ldfield","target"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3"),Ins("pop"),Ins("ret"))
    };
    for(int i=0;i<bodies.Length;i++)using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
     var candidate=BodyBuild(build,bodies[i]);var host=new Host(domain){Supported=true,Result=i==2?(object)new VectorValue("vector3",number,0,0):null};
     var runtime=new VmRuntime(candidate,domain,host,new ExecutionQuota(1000,16,1000));runtime.Start();
     if(outside){Reject(()=>runtime.Update(),"vector-or-color-range-not-enforced:"+i);if(i<2)Assert(host.Calls==0,"out-of-range-argument-entered-host");}
     else{runtime.Update();Assert(host.Calls==1,"valid-vector-or-color-boundary-rejected");}
    }
   }
   Console.WriteLine("PASS vector/color parameter and return inclusive ranges");
   var callBuild=HostCallBuild(build);
   ExecutionBindings.Require(callBuild,RenderingBindings.Supports);
   var unbound=BodyBuild(build,new JArray(Ins("ldstr","test"),Ins("call","SpatialWallpaper.Sdk::Llm::BeginPrompt(System.String)->System.Int32"),Ins("pop"),Ins("ret")));
   Reject(()=>ExecutionBindings.Require(unbound,RenderingBindings.Supports),"unbound-capability-passed-pre-native-preflight");
   using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
    var host=new Host(domain);Reject(()=>new VmRuntime(callBuild,domain,host,new ExecutionQuota(1000,16,1000)),"missing-binding-accepted");
    Assert(host.Resolved==0,"missing-binding-created-script-fields");
   }
   using(var foreign=new OwnedDomain(Guid.NewGuid(),1)){
    foreach(var result in new object[]{new object(),foreign.Register("mesh",new object())}){
     using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
      var host=new Host(domain){Supported=true,Result=result};
      var runtime=new VmRuntime(callBuild,domain,host,new ExecutionQuota(1000,16,1000));runtime.Start();
      Reject(()=>runtime.Update(),"private-or-foreign-native-return");
      Assert(runtime.State==VmState.Faulted,"native-return-did-not-fault");
     }
    }
   }
   Console.WriteLine("PASS binding-before-callback and private/foreign native returns");
   Console.WriteLine("PASS lifecycle and Inspector binding");
   if(args.Length>1){
    foreach(string path in args.Skip(1).TakeWhile(path=>path!="--instructions"))using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
     var hostile=Admission.Admit(File.ReadAllBytes(path),"windows-x86_64");
     var execution=new VmRuntime(hostile,domain,new Host(domain),new ExecutionQuota(1000,16,1000));execution.Start();
     Reject(()=>execution.Update(),"loop-or-recursion-did-not-stop");
     Assert(execution.State==VmState.Faulted,"fault-state");
     Reject(()=>execution.Update(),"faulted-runtime-reentered");
    }
    Console.WriteLine("PASS loop/recursion stop and no reentry");
   }
   InventoryChecks.Run();
   int instructionIndex=System.Array.IndexOf(args,"--instructions");if(instructionIndex>=0)InstructionChecks.Run(Admission.Admit(File.ReadAllBytes(args[instructionIndex+1]),"windows-x86_64"));
   return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

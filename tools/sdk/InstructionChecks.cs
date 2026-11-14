using System;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Security;using Newtonsoft.Json.Linq;
static class InstructionChecks {
 sealed class Host:IVmHost {
  readonly OwnedDomain owner;public Host(OwnedDomain domain){owner=domain;}
  public bool Supports(string member)=>false;
  public object Resolve(string type,JToken value,int scope)=>owner.Register(type,new object());
  public object Call(string member,object receiver,object[] args){throw new Exception("unexpected-host-call");}
 }
 static void Assert(bool yes,string name){if(!yes)throw new Exception("instruction-result:"+name);}
 internal static void Run(AdmittedBuild build){
  using(var owner=new OwnedDomain(Guid.NewGuid(),1)){
   var vm=new VmRuntime(build,owner,new Host(owner),new ExecutionQuota(5000,16,1000));vm.Start();vm.Update();
   Assert((int)vm.Field(0,"counter")==10&&(int)vm.Field(0,"arithmetic")==2,"integer");
   Assert(Math.Abs((float)vm.Field(0,"ratio")-4.5f)<.001f&&(bool)vm.Field(0,"flag"),"float-bool-branch");
   Assert((string)vm.Field(0,"text")=="Sdk","string-null");
   var items=(OwnedArray)vm.Field(0,"items");Assert(items.Count==2&&(int)owner.Get(items,0)==10&&(int)owner.Get(items,1)==0,"array-loop-invoke");
   var vector=(VectorValue)vm.Field(0,"vector");var color=(VectorValue)vm.Field(0,"color");Assert(vector.X==1&&vector.Y==2&&vector.Z==3&&Math.Abs(color.W-.4f)<.001f,"vector-color");
   var data=(ScriptObject)vm.Field(0,"data");Assert(data.ClassId=="Data"&&(int)data.Fields["v"]==3,"new-object-field");
   var list=(OwnedList)vm.Field(0,"list");Assert(list.Count==1&&(int)owner.Get(list,0)==2,"closed-list");
   vm.Stop();
  }
  Console.WriteLine("PASS executable arithmetic, boolean, branches, locals, arrays, objects, helper calls, vectors and closed-list operations");
 }
}

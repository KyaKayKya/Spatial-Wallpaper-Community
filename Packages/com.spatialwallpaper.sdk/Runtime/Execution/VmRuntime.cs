using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Execution {
 public interface IVmHost {
  bool Supports(string member);
  object Resolve(string type,JToken value,int scope);
  object Call(string member,object receiver,object[] arguments);
 }
 // Optional script bridge gate. Legacy hosts retain their existing lifecycle.
 public interface IVmExecutionGate { void RequireExecution(string callback); }
 public enum VmState {Created,Running,Stopped,Faulted}
 public sealed class ExecutionQuota {
  public readonly int Instructions,Depth,Milliseconds;
  public ExecutionQuota(int instructions,int depth,int milliseconds){
   if(instructions<1||depth<1||milliseconds<1)throw new ArgumentOutOfRangeException();
   Instructions=instructions;Depth=depth;Milliseconds=milliseconds;
  }
 }
 public sealed class ExecutionBudgetException:InvalidOperationException { public ExecutionBudgetException():base("execution-budget"){} }
 public sealed class VmRuntime {
  readonly OwnedDomain domain;readonly IVmHost host;readonly ExecutionQuota quota;
  sealed class Method {
   internal string Class,Returns;internal string[] Parameters,Locals;internal Instruction[] Body;internal int Stack;
  }
  readonly struct Instruction {
   internal readonly string Op;internal readonly object Value;
   internal Instruction(JObject source){
    Op=(string)source["op"];var value=source["value"];
    Value=value.Type==JTokenType.Null?null:Op=="ldc.r4"?(object)(float)value:value.Type==JTokenType.Integer?(object)(int)value:(string)value;
   }
  }
  readonly Dictionary<string,JObject> classes;
  readonly Dictionary<string,Dictionary<string,string>> fieldTypes=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
  readonly Dictionary<string,Method> methods=new Dictionary<string,Method>(StringComparer.Ordinal);
  readonly Dictionary<string,Dictionary<string,string>> callbackIds=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
  readonly Dictionary<string,JObject> members=new Dictionary<string,JObject>(StringComparer.Ordinal);
  readonly List<ScriptObject> roots;
  readonly List<Tuple<ScriptObject,string[],int,int>> callbacks;
  readonly HashSet<ScriptObject> started;
  readonly ResourceLease programMemory;AllocationFrame allocation;
  int remaining,depth;long deadline;
  public VmState State{get;private set;}=VmState.Created;
  public VmRuntime(AdmittedBuild build,OwnedDomain owner,IVmHost bindings,ExecutionQuota executionQuota){
   domain=owner??throw new ArgumentNullException(nameof(owner));host=bindings??throw new ArgumentNullException(nameof(bindings));quota=executionQuota??throw new ArgumentNullException(nameof(executionQuota));
   ExecutionBindings.Require(build,host.Supports);
   domain.AttachBuild(build);
   programMemory=domain.Ledger.Reserve(new ResourceCost(checked(build.ProgramCloningBytes*2+build.ContentCloningBytes*2+4096+domain.Ledger.Limit.Objects*64+RuleCatalog.Current.Limit("nodes")*16L),0,0,0));
   try{
    classes=((JArray)build.Program["classes"]).Cast<JObject>().ToDictionary(c=>(string)c["id"],StringComparer.Ordinal);
    roots=new List<ScriptObject>(RuleCatalog.Current.Limit("nodes"));
    callbacks=new List<Tuple<ScriptObject,string[],int,int>>((int)domain.Ledger.Limit.Objects);
    started=new HashSet<ScriptObject>((int)domain.Ledger.Limit.Objects);
    foreach(var cls in classes){
     callbackIds.Add(cls.Key,new Dictionary<string,string>(StringComparer.Ordinal));
     fieldTypes.Add(cls.Key,((JArray)cls.Value["fields"]).Cast<JObject>().ToDictionary(f=>(string)f["id"],f=>(string)f["type"],StringComparer.Ordinal));
     foreach(JObject source in (JArray)cls.Value["methods"]){
      string id=cls.Key+"."+(string)source["id"];
      var method=new Method{Class=cls.Key,Returns=(string)source["returns"],Parameters=((JArray)source["parameters"]).Select(t=>(string)t).ToArray(),Locals=((JArray)source["locals"]).Select(t=>(string)t).ToArray(),Body=((JArray)source["body"]).Cast<JObject>().Select(i=>new Instruction(i)).ToArray(),Stack=build.MethodStack(id)};
      methods.Add(id,method);
      if((string)source["id"]=="Start"||(string)source["id"]=="Update")callbackIds[cls.Key].Add((string)source["id"],id);
      foreach(var instruction in method.Body)if(instruction.Op=="call"){
       string member=(string)instruction.Value;
       if(!CoreLibrary.Supports(member)&&!host.Supports(member))throw new InvalidOperationException("missing-host-binding:"+member);
       if(!members.ContainsKey(member))members.Add(member,build.Rules.Member(member));
      }
     }
    }
    BindGraph((JArray)build.Content["nodes"],0,true);programMemory.Commit();
   }catch{programMemory.Dispose();throw;}
  }
  internal List<ScriptObject> BindGraph(JArray nodes,int scope,bool isRoot=false){
   long checkpoint=domain.Checkpoint();int callbackMark=callbacks.Count;
   try{
   var objects=new List<ScriptObject>();
   foreach(JObject node in nodes){
    var script=((JArray)node["components"]).Cast<JObject>().SingleOrDefault(c=>(string)c["kind"]=="script");
    objects.Add(script==null?null:Create((string)script["config"]["class"]));
   }
   for(int i=0;i<nodes.Count;i++){
    if(objects[i]==null)continue;
    var script=((JArray)nodes[i]["components"]).Cast<JObject>().Single(c=>(string)c["kind"]=="script");
    foreach(JObject field in (JArray)classes[objects[i].ClassId]["fields"]){
     string id=(string)field["id"],type=(string)field["type"];object value=ReadValue(type,script["fields"][id],scope,objects);domain.RequireType(value,type);objects[i].Fields[id]=value;
    }
    callbacks.Add(Tuple.Create(objects[i],((JArray)script["callbacks"]).Select(c=>(string)c).ToArray(),scope,i));
   }
   if(isRoot)roots.AddRange(objects);
   return objects;
   }catch{callbacks.RemoveRange(callbackMark,callbacks.Count-callbackMark);domain.Rollback(checkpoint);throw;}
  }
  internal void DetachNodes(int scope,HashSet<int> nodes){
   if(scope==0)foreach(int node in nodes)roots[node]=null;
   foreach(var entry in callbacks.Where(c=>c.Item3==scope&&nodes.Contains(c.Item4)).ToArray()){domain.InvalidateValue(entry.Item1);started.Remove(entry.Item1);}
   callbacks.RemoveAll(c=>c.Item3==scope&&nodes.Contains(c.Item4));
  }
  ScriptObject Create(string id){
   var value=domain.Object(id,fieldTypes[id].Count);
   foreach(JObject field in (JArray)classes[id]["fields"])value.Fields.Add((string)field["id"],Default((string)field["type"]));
   return value;
  }
  static object Default(string type)=>OwnedDomain.DefaultValue(type);
  object ReadValue(string type,JToken value,int scope,List<ScriptObject> objects){
   if(value.Type==JTokenType.Null)return null;
   if(type.EndsWith("[]",StringComparison.Ordinal)){string element=type.Substring(0,type.Length-2);return domain.CreateArray(element,((JArray)value).Count,i=>ReadValue(element,value[i],scope,objects));}
   string listElement=RuleCatalog.Current.ListElement(type);if(listElement!=null)return domain.CreateList(listElement,((JArray)value).Count,i=>ReadValue(listElement,value[i],scope,objects));
   switch(type){
    case "i32":return (int)value;case "f32":return (float)value;case "bool":return (bool)value;case "string":return (string)value;
    case "vector3":case "color":return new VectorValue(type,(float)value[0],(float)value[1],(float)value[2],type=="color"?(float)value[3]:0);
    default:
     if(classes.ContainsKey(type)){
      if(value["script"]!=null)return objects[(int)value["script"]];
      var obj=Create(type);foreach(JObject field in (JArray)classes[type]["fields"])obj.Fields[(string)field["id"]]=ReadValue((string)field["type"],value["fields"][(string)field["id"]],scope,objects);return obj;
     }
     object native=host.Resolve(type,value,scope);domain.RequireType(native,type);return native;
   }
  }
  public object Field(int node,string name){if(roots[node]==null)throw new InvalidOperationException("removed-script-root");domain.Check(roots[node]);var value=roots[node].Fields[name];domain.Check(value);return value;}
  void Begin(){allocation=AllocationFrame.Begin(domain.Ledger);remaining=quota.Instructions;depth=0;deadline=Stopwatch.GetTimestamp()+(long)(quota.Milliseconds/1000.0*Stopwatch.Frequency);}
  void Charge(long bytes){allocation.Charge(bytes);}
  void Step(){if(--remaining<0||Stopwatch.GetTimestamp()>deadline)throw new ExecutionBudgetException();}
  void RunCallbacks(string callback){
   try{
    Begin();Charge(32+callbacks.Count*8L);
    var snapshot=callbacks.ToArray();
    // Newly instantiated scripts cannot join this callback iteration.
    foreach(var registered in snapshot){
     if(!registered.Item1.Alive)continue;
     bool first=started.Add(registered.Item1);
     if(first&&System.Array.IndexOf(registered.Item2,"Start")>=0){Charge(32);Invoke(callbackIds[registered.Item1.ClassId]["Start"],new object[]{registered.Item1});}
     if(callback!="Start"&&registered.Item1.Alive&&System.Array.IndexOf(registered.Item2,callback)>=0){Charge(32);Invoke(callbackIds[registered.Item1.ClassId][callback],new object[]{registered.Item1});}
    }
   }catch{State=VmState.Faulted;throw;}
   finally{allocation=null;}
  }
  public void Start(){if(State!=VmState.Created)throw new InvalidOperationException("start-state");(host as IVmExecutionGate)?.RequireExecution("Start");State=VmState.Running;RunCallbacks("Start");}
  public void Update(){if(State!=VmState.Running)throw new InvalidOperationException("update-state");(host as IVmExecutionGate)?.RequireExecution("Update");RunCallbacks("Update");}
  public void Stop(){if(State==VmState.Stopped)return;State=VmState.Stopped;callbacks.Clear();started.Clear();programMemory.Dispose();domain.Dispose();}
  object Pop(object[] stack,ref int position){
   object value=stack[--position];stack[position]=null;domain.CheckDirect(value);return value;
  }
  internal object Invoke(string id,object[] arguments){
   Step();if(++depth>quota.Depth){depth--;throw new ExecutionBudgetException();}
   try{
    var method=methods[id];string cls=method.Class;
    if(arguments[0]==null)throw new NullReferenceException("null-invoke-receiver");domain.RequireType(arguments[0],cls);
    if(arguments.Length!=method.Parameters.Length+1)throw new InvalidOperationException("argument-count");
    for(int i=0;i<method.Parameters.Length;i++)domain.RequireType(arguments[i+1],method.Parameters[i]);
    Charge(64+method.Locals.Length*72L+method.Stack*8L);
    var locals=new object[method.Locals.Length];for(int i=0;i<locals.Length;i++)locals[i]=Default(method.Locals[i]);
    var stack=new object[method.Stack];int position=0,pc=0;
    while(true){
     Step();var instruction=method.Body[pc++];string op=instruction.Op;object operand=instruction.Value;
     switch(op){
      case "nop":break;case "ldarg":stack[position++]=arguments[(int)operand];break;
      case "ldloc":stack[position++]=locals[(int)operand];break;
      case "stloc":var local=Pop(stack,ref position);domain.RequireType(local,method.Locals[(int)operand]);locals[(int)operand]=local;break;
      case "ldfield":var obj=(ScriptObject)Pop(stack,ref position);domain.CheckDirect(obj);stack[position++]=obj.Fields[(string)operand];break;
      case "stfield":var fieldValue=Pop(stack,ref position);var target=(ScriptObject)Pop(stack,ref position);domain.CheckDirect(target);domain.RequireType(fieldValue,fieldTypes[target.ClassId][(string)operand]);target.Fields[(string)operand]=fieldValue;break;
      case "ldc.i4":case "ldc.r4":case "ldstr":stack[position++]=operand;break;case "ldnull":stack[position++]=null;break;
      case "dup":var copy=Pop(stack,ref position);stack[position++]=copy;stack[position++]=copy;break;case "pop":Pop(stack,ref position);break;
      case "add":case "sub":case "mul":case "div":case "rem":case "and":case "or":case "xor":case "ceq":case "clt":case "cgt":case "cle":case "cge":case "clt.un":case "cgt.un":case "cle.un":case "cge.un":
       var b=Pop(stack,ref position);var a=Pop(stack,ref position);Charge(64);stack[position++]=Binary(op,a,b);break;
      case "neg":var negative=Pop(stack,ref position);Charge(32);stack[position++]=negative is int integer?(object)unchecked(-integer):-(float)negative;break;
      case "not":int bits=(int)Pop(stack,ref position);Charge(32);stack[position++]=~bits;break;
      case "conv.bool":bool truth=Truth(Pop(stack,ref position));Charge(32);stack[position++]=truth;break;
      case "conv.i4":double number=Math.Truncate(Convert.ToDouble(Pop(stack,ref position)));Charge(32);stack[position++]=unchecked(Convert.ToInt32(number));break;
      case "conv.r4":float single=Convert.ToSingle(Pop(stack,ref position));Charge(32);stack[position++]=single;break;
      case "newobj":stack[position++]=Create((string)operand);break;
      case "newlist":int capacity=(int)Pop(stack,ref position);stack[position++]=domain.List(RuleCatalog.Current.ListElement((string)operand),System.Array.Empty<object>(),capacity);break;
      case "newvector3":float z=(float)Pop(stack,ref position),y=(float)Pop(stack,ref position),x=(float)Pop(stack,ref position);Charge(64);stack[position++]=new VectorValue("vector3",x,y,z);break;
      case "newcolor":float alpha=(float)Pop(stack,ref position),blue=(float)Pop(stack,ref position),green=(float)Pop(stack,ref position),red=(float)Pop(stack,ref position);Charge(64);stack[position++]=new VectorValue("color",red,green,blue,alpha);break;
      case "newarray":int length=(int)Pop(stack,ref position);if(length<0||length>RuleCatalog.Current.Limit("resources"))throw new InvalidOperationException("array-size");
       stack[position++]=domain.CreateArray((string)operand,length,null);break;
      case "ldlen":int count=((OwnedArray)Pop(stack,ref position)).Count;Charge(32);stack[position++]=count;break;
       case "ldelem":case "ldelem.ref":int index=(int)Pop(stack,ref position);var array=(OwnedArray)Pop(stack,ref position);stack[position++]=domain.Get(array,index);break;
       case "stelem":case "stelem.ref":var item=Pop(stack,ref position);int offset=(int)Pop(stack,ref position);domain.Set((OwnedArray)Pop(stack,ref position),offset,item);break;
       case "getcomponent":var vector=(VectorValue)Pop(stack,ref position);Charge(32);stack[position++]=(int)operand==0?vector.X:(int)operand==1?vector.Y:(int)operand==2?vector.Z:vector.W;break;
      case "call":
       string memberId=(string)operand;var member=members[memberId];var types=(JArray)member["arguments"];
       Charge(32+types.Count*8L);var values=new object[types.Count];
       for(int i=values.Length-1;i>=0;i--){values[i]=Pop(stack,ref position);Constraint(values[i],(JObject)member["argument_constraints"][i]);}
       object receiver=(string)member["receiver"]==""?null:Pop(stack,ref position);
       if(receiver!=null||((string)member["receiver"]).Length>0)Constraint(receiver,(JObject)member["receiver_constraint"]);
       Charge(128);Step();object result=CoreLibrary.Supports(memberId)?CoreLibrary.Call(domain,memberId,receiver,values):host.Call(memberId,receiver,values);Step();
       if((string)member["returns"]!="void"){Constraint(result,(JObject)member["return_constraint"]);stack[position++]=result;}else if(result!=null)throw new InvalidOperationException("void-host-result");
       break;
      case "invoke":
       var invoked=methods[(string)operand];Charge(32+(invoked.Parameters.Length+1)*8L);
       var callArgs=new object[invoked.Parameters.Length+1];for(int i=callArgs.Length-1;i>=0;i--)callArgs[i]=Pop(stack,ref position);
       var returnValue=Invoke((string)operand,callArgs);if(invoked.Returns!="void")stack[position++]=returnValue;break;
      case "br":pc=(int)operand;break;case "brtrue":if(BranchTruth(Pop(stack,ref position)))pc=(int)operand;break;case "brfalse":if(!BranchTruth(Pop(stack,ref position)))pc=(int)operand;break;
      case "ret":object answer=method.Returns=="void"?null:Pop(stack,ref position);if(method.Returns!="void")domain.RequireType(answer,method.Returns);return answer;
      default:throw new InvalidOperationException("unbound-opcode");
     }
    }
   }finally{depth--;}
  }
  void Constraint(object value,JObject constraint){
   string type=(string)constraint["type"];domain.RequireType(value,type);
   if(value==null){if(!(bool)constraint["nullable"])throw new InvalidOperationException("null-argument");return;}
   if(type=="string"&&((string)value).Length>(int)constraint["max_chars"])throw new InvalidOperationException("argument-string-limit");
   if(type=="i32"||type=="f32")NumberRange(Convert.ToDouble(value),constraint);
   if(type=="vector3"||type=="color"){
    var vector=(VectorValue)value;NumberRange(vector.X,constraint);NumberRange(vector.Y,constraint);NumberRange(vector.Z,constraint);if(type=="color")NumberRange(vector.W,constraint);
   }
  }
  static void NumberRange(double value,JObject constraint){
   if(double.IsNaN(value)||double.IsInfinity(value)||value<(double)constraint["min"]||value>(double)constraint["max"])throw new InvalidOperationException("argument-number-range");
  }
  static bool Truth(object value)=>value is bool b?b:(int)value!=0;
  static bool BranchTruth(object value){
   if(value==null)return false;if(value is bool b)return b;if(value is int i)return i!=0;
   if(value is string||value is OwnedValue)return true;
   throw new InvalidOperationException("condition-type");
  }
  static object Binary(string op,object a,object b){
   // CLI's reference cgt.un form is a non-null test, not reference ordering.
   if(op=="cgt.un"&&b==null&&(a==null||a is string||a is OwnedValue))return a!=null;
   if(op=="ceq"){
    if(a is float fa&&b is float fb)return fa==fb;
    if(a is VectorValue va&&b is VectorValue vb)return va.Type==vb.Type&&va.X==vb.X&&va.Y==vb.Y&&va.Z==vb.Z&&va.W==vb.W;
    return a is bool&&b is int||a is int&&b is bool?Convert.ToInt32(a)==Convert.ToInt32(b):Equals(a,b);
   }
   if(a is bool ba&&b is bool bb){switch(op){case "and":return ba&bb;case "or":return ba|bb;case "xor":return ba^bb;}}
   // CIL uses the integer evaluation stack for bool. Preserve bit patterns:
   // true & 2 is zero, not logical true. Mixed operations produce an i32.
   if(op=="and"||op=="or"||op=="xor"){
    if(a is bool leftBoolean&&b is int)a=leftBoolean?1:0;
    if(b is bool rightBoolean&&a is int)b=rightBoolean?1:0;
   }
   if(a is int x&&b is int y){
    switch(op){case "cle":return x<=y;case "cge":return x>=y;case "clt.un":return unchecked((uint)x)<unchecked((uint)y);case "cgt.un":return unchecked((uint)x)>unchecked((uint)y);case "cle.un":return unchecked((uint)x)<=unchecked((uint)y);case "cge.un":return unchecked((uint)x)>=unchecked((uint)y);}
    switch(op){case "add":return unchecked(x+y);case "sub":return unchecked(x-y);case "mul":return unchecked(x*y);case "div":return x/y;case "rem":return x%y;case "and":return x&y;case "or":return x|y;case "xor":return x^y;case "clt":return x<y;case "cgt":return x>y;}
   }
   float left=(float)a,right=(float)b;
   bool unordered=float.IsNaN(left)||float.IsNaN(right);
   switch(op){case "cle":return left<=right;case "cge":return left>=right;case "clt.un":return unordered||left<right;case "cgt.un":return unordered||left>right;case "cle.un":return unordered||left<=right;case "cge.un":return unordered||left>=right;}
   switch(op){case "add":return left+right;case "sub":return left-right;case "mul":return left*right;case "div":return left/right;case "rem":return left%right;case "clt":return left<right;case "cgt":return left>right;default:throw new InvalidOperationException("numeric-opcode");}
  }
 }
}

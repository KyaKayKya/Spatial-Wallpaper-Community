using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Security {
 // No type resolution, reflection, delegate construction or author execution in admission.
 internal sealed class ProgramContract {
  readonly JObject data;
  readonly Dictionary<string,JObject> classes=new Dictionary<string,JObject>(StringComparer.Ordinal);
  readonly Dictionary<string,JObject> methods=new Dictionary<string,JObject>(StringComparer.Ordinal);
  readonly HashSet<string> capabilities=new HashSet<string>(StringComparer.Ordinal);
  readonly RuleCatalog rules;
  int instructionTotal;
  internal readonly Dictionary<string,int> StackLimits=new Dictionary<string,int>(StringComparer.Ordinal);
  public string[] Capabilities=>capabilities.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
  public ProgramContract(JObject value,RuleCatalog catalog=null){
   rules=catalog??RuleCatalog.Current;
   data=value;JsonData.Fields(data,"classes");
   var list=JsonData.Array(data["classes"],rules.Limit("classes"));
   foreach(JObject cls in list){
    JsonData.Fields(cls,"id","fields","methods");string id=Name(cls["id"]);Need(!rules.IsType(id),"reserved-class-name");
    Need(!classes.ContainsKey(id),"duplicate-class");classes.Add(id,cls);
   }
   foreach(var pair in classes){
    var cls=pair.Value;var names=new HashSet<string>(StringComparer.Ordinal);
    foreach(JObject f in JsonData.Array(cls["fields"],rules.Limit("fields"))){JsonData.Fields(f,"id","type");Need(names.Add(Name(f["id"])),"duplicate-field");Type(f["type"],false);}
    foreach(JObject method in JsonData.Array(cls["methods"],rules.Limit("methods"))){
     JsonData.Fields(method,"id","returns","parameters","locals","body");
     string id=Name(method["id"]);Need(!methods.ContainsKey(pair.Key+"."+id),"duplicate-method");methods.Add(pair.Key+"."+id,method);
     Type(method["returns"],true);
     foreach(var t in JsonData.Array(method["parameters"],rules.Limit("parameters")))Type(t,false);
     foreach(var t in JsonData.Array(method["locals"],rules.Limit("locals")))Type(t,false);
    }
   }
   foreach(var pair in methods)ValidateBody(pair.Key,pair.Value);
  }
  internal static void Need(bool yes,string code)=>JsonData.Require(yes,code);
  internal static string Name(JToken token){
   string name=JsonData.Text(token,96);Need(name.Length>0&&name.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'),"identifier");
   Need(!(name[0]>='0'&&name[0]<='9'),"identifier-start");return name;
  }
  string Type(JToken token,bool allowVoid){
   string type=JsonData.Text(token);
   if(type.EndsWith("[]",StringComparison.Ordinal)){string item=type.Substring(0,type.Length-2);Need((rules.IsType(item)||classes.ContainsKey(item))&&item!="void","array-type");return type;}
   Need((rules.IsType(type)||classes.ContainsKey(type))&&(allowVoid||type!="void"),"type-not-allowed:"+type);return type;
  }
  static bool Numeric(string type)=>type=="i32"||type=="f32";
  bool Fits(string actual,string expected)=>actual==expected||actual=="null"&&(classes.ContainsKey(expected)||expected.EndsWith("[]",StringComparison.Ordinal)||rules.IsType(expected)&&(bool)rules.Constraint(expected)["nullable"]);
  bool Reference(string type)=>Fits("null",type);
  JObject Field(string cls,string name){
   Need(classes.ContainsKey(cls),"field-receiver-type");
   var f=((JArray)classes[cls]["fields"]).Cast<JObject>().SingleOrDefault(x=>(string)x["id"]==name);
   Need(f!=null,"field-not-declared:"+name);return f;
  }
  void ValidateBody(string key,JObject method){
   var body=JsonData.Array(method["body"],rules.Limit("method_instructions"));Need(body.Count>0,"empty-body");
   instructionTotal+=body.Count;Need(instructionTotal<=rules.Limit("program_instructions"),"program-instruction-limit");
   string cls=key.Substring(0,key.IndexOf('.'));var args=new List<string>{cls};args.AddRange(((JArray)method["parameters"]).Select(x=>(string)x));
   var locals=((JArray)method["locals"]).Select(x=>(string)x).ToArray();string returns=(string)method["returns"];
   // Validate even unreachable instructions and references before flow traversal.
   foreach(JObject ins in body){
    JsonData.Fields(ins,"op","value");string op=JsonData.Text(ins["op"]);string operand=rules.Operand(op);JToken val=ins["value"];
    switch(operand){
     case "none":Need(val.Type==JTokenType.Null,"unexpected-operand");break;
     case "i32":JsonData.Integer(val,int.MinValue,int.MaxValue);break;
     case "f32":JsonData.Number(val,-float.MaxValue,float.MaxValue);break;
     case "string":JsonData.Text(val,4096);break;
     case "type":Type(val,false);break;
     case "class":Need(classes.ContainsKey(JsonData.Text(val)),"unknown-class");break;
     case "list":Need(rules.ListElement(JsonData.Text(val))!=null,"list-type");break;
     case "field":Name(val);break;
     case "index":JsonData.Integer(val,0,op=="ldarg"?args.Count-1:locals.Length-1);break;
     case "component":JsonData.Integer(val,0,3);break;
     case "branch":JsonData.Integer(val,0,body.Count-1);break;
     case "method":Need(methods.ContainsKey(JsonData.Text(val)),"unknown-method");break;
     case "member":var native=rules.Member(JsonData.Text(val));string cap=(string)native["capability"];if(cap.Length>0)capabilities.Add(cap);break;
     default:throw new InvalidDataException("catalog-operand");
    }
   }
   var states=new Dictionary<int,string[]>();var pending=new Queue<int>();
   Action<int,List<string>> merge=(pc,stack)=>{
    Need(pc>=0&&pc<body.Count,"fallthrough");Need(stack.Count<=rules.Limit("stack"),"stack-limit");
    if(states.TryGetValue(pc,out var previous)){
     Need(previous.Length==stack.Count,"incompatible-stack-join");bool changed=false;var joined=(string[])previous.Clone();
     for(int i=0;i<joined.Length;i++)if(joined[i]!=stack[i]){
      if(joined[i]=="null"&&Reference(stack[i])){joined[i]=stack[i];changed=true;}
      else Need(stack[i]=="null"&&Reference(joined[i]),"incompatible-stack-join");
     }
     if(changed){states[pc]=joined;pending.Enqueue(pc);}
    }else{states.Add(pc,stack.ToArray());pending.Enqueue(pc);}
   };
   merge(0,new List<string>());
   while(pending.Count>0){
    int pc=pending.Dequeue();var stack=states[pc].ToList();var ins=(JObject)body[pc];string op=(string)ins["op"];JToken val=ins["value"];
    Func<string> pop=()=>{Need(stack.Count>0,"stack-underflow");string t=stack[stack.Count-1];stack.RemoveAt(stack.Count-1);return t;};
    Action<string> expect=t=>Need(Fits(pop(),t),"stack-type:"+key+":"+pc);
    bool next=true;
    switch(op){
     case "nop":break;
     case "ldarg":stack.Add(args[(int)val]);break;
     case "ldloc":stack.Add(locals[(int)val]);break;
     case "stloc":expect(locals[(int)val]);break;
     case "ldfield":stack.Add((string)Field(pop(),(string)val)["type"]);break;
     case "stfield":string value=pop();string receiver=pop();Need(Fits(value,(string)Field(receiver,(string)val)["type"]),"field-type");break;
     case "ldc.i4":stack.Add("i32");break;
     case "ldc.r4":stack.Add("f32");break;
     case "ldstr":stack.Add("string");break;
     case "ldnull":stack.Add("null");break;
     case "dup":string duplicate=pop();stack.Add(duplicate);stack.Add(duplicate);break;
     case "pop":pop();break;
     case "add":case "sub":case "mul":case "div":case "rem":
      string right=pop(),left=pop();Need(Numeric(left)&&left==right,"arithmetic-type");stack.Add(left);break;
     case "ceq":case "clt":case "cgt":case "cle":case "cge":case "clt.un":case "cgt.un":case "cle.un":case "cge.un":
      string b=pop(),a=pop();Need(a==b||Fits(a,b)||Fits(b,a)||op=="ceq"&&(a=="bool"&&b=="i32"||a=="i32"&&b=="bool"),"comparison-type");if(op!="ceq")Need(Numeric(a)||op=="cgt.un"&&b=="null"&&Reference(a),"ordered-comparison");stack.Add("bool");break;
     case "and":case "or":case "xor":string bits=pop(),otherBits=pop();Need((bits=="i32"||bits=="bool")&&(otherBits=="i32"||otherBits=="bool"),"bitwise-type");stack.Add(bits=="bool"&&otherBits=="bool"?"bool":"i32");break;
     case "not":expect("i32");stack.Add("i32");break;
     case "neg":string n=pop();Need(Numeric(n),"numeric-negation");stack.Add(n);break;
     case "conv.bool":string boolean=pop();Need(boolean=="bool"||boolean=="i32","boolean-conversion");stack.Add("bool");break;
     case "conv.i4":case "conv.r4":Need(Numeric(pop()),"numeric-conversion");stack.Add(op=="conv.i4"?"i32":"f32");break;
     case "newobj":stack.Add((string)val);break;
     case "newlist":expect("i32");stack.Add((string)val);break;
     case "newvector3":expect("f32");expect("f32");expect("f32");stack.Add("vector3");break;
     case "newcolor":expect("f32");expect("f32");expect("f32");expect("f32");stack.Add("color");break;
     case "newarray":expect("i32");Need(!((string)val).EndsWith("[]",StringComparison.Ordinal),"nested-array");stack.Add((string)val+"[]");break;
     case "ldlen":Need(pop().EndsWith("[]",StringComparison.Ordinal),"array-receiver");stack.Add("i32");break;
     case "ldelem":expect("i32");expect((string)val+"[]");stack.Add((string)val);break;
     case "stelem":expect((string)val);expect("i32");expect((string)val+"[]");break;
      case "ldelem.ref":expect("i32");string referenceArray=pop();Need(referenceArray.EndsWith("[]",StringComparison.Ordinal),"reference-array-receiver");string element=referenceArray.Substring(0,referenceArray.Length-2);Need(Reference(element),"reference-array-element");stack.Add(element);break;
      case "stelem.ref":string referenceValue=pop();expect("i32");string storedArray=pop();Need(storedArray.EndsWith("[]",StringComparison.Ordinal),"reference-array-receiver");string storedElement=storedArray.Substring(0,storedArray.Length-2);Need(Reference(storedElement)&&Fits(referenceValue,storedElement),"reference-array-store-type");break;
      case "getcomponent":string vector=pop();Need(vector=="color"||vector=="vector3"&&(int)val<3,"vector-component-receiver");stack.Add("f32");break;
     case "call":var member=rules.Member((string)val);foreach(string t in ((JArray)member["arguments"]).Reverse())expect(t);
      if((string)member["receiver"]!="")expect((string)member["receiver"]);if((string)member["returns"]!="void")stack.Add((string)member["returns"]);break;
     case "invoke":var target=methods[(string)val];foreach(string t in ((JArray)target["parameters"]).Reverse())expect(t);
      expect(((string)val).Split('.')[0]);if((string)target["returns"]!="void")stack.Add((string)target["returns"]);break;
     case "br":merge((int)val,stack);next=false;break;
     case "brtrue":case "brfalse":string condition=pop();Need(condition=="bool"||condition=="i32"||Reference(condition),"condition-type");merge((int)val,stack);break;
     case "ret":if(returns!="void")expect(returns);Need(stack.Count==0,"return-stack");next=false;break;
     default:throw new InvalidDataException("unhandled-opcode");
    }
    if(next)merge(pc+1,stack);
   }
   // Unreachable instructions are rejected, not silently removed by the exporter.
   Need(states.Count==body.Count,"unreachable-instructions");
   StackLimits.Add(key,states.Values.Max(state=>state.Length));
  }
  public void Value(string type,JToken value,Action<string,JToken> reference,Func<int,string> scriptType,int nodeCount,int depth=0){
   Need(depth<=16,"field-value-depth");
   if(type.EndsWith("[]",StringComparison.Ordinal)){
    if(value.Type==JTokenType.Null)return;
    foreach(var item in JsonData.Array(value,(int)rules.Constraint(type)["max_elements"]))Value(type.Substring(0,type.Length-2),item,reference,scriptType,nodeCount,depth+1);
    return;
   }
   string listElement=rules.ListElement(type);
   if(listElement!=null){
    if(value.Type==JTokenType.Null)return;
    foreach(var item in JsonData.Array(value,(int)rules.Constraint(type)["max_elements"]))Value(listElement,item,reference,scriptType,nodeCount,depth+1);return;
   }
   if(!classes.ContainsKey(type)){ValueContract.Check(value,rules.Constraint(type),reference);return;}
   if(value.Type==JTokenType.Null)return;
   var data=value as JObject;
   if(data!=null&&data.ContainsKey("script")){
    JsonData.Fields(data,"script");int index=JsonData.Integer(data["script"],0,nodeCount-1);Need(scriptType(index)==type,"script-reference-type");return;
   }
   JsonData.Fields(data,"fields");var fields=data["fields"] as JObject;var declared=(JArray)classes[type]["fields"];
   JsonData.Fields(fields,declared.Select(f=>(string)f["id"]).ToArray());
   foreach(JObject field in declared)Value((string)field["type"],fields[(string)field["id"]],reference,scriptType,nodeCount,depth+1);
  }
  public void Binding(string cls,JObject fields,JArray callbacks,Action<string,JToken> validate){
   Need(classes.ContainsKey(cls),"unknown-script-class");var declared=(JArray)classes[cls]["fields"];
   JsonData.Fields(fields,declared.Select(f=>(string)f["id"]).ToArray());
   foreach(JObject field in declared)validate((string)field["type"],fields[(string)field["id"]]);
   var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var cb in JsonData.Array(callbacks,2)){
    string name=JsonData.Text(cb);Need(((JArray)rules.Snapshot["callbacks"]).Any(x=>(string)x==name)&&seen.Add(name),"callback-not-allowed");
    Need(methods.TryGetValue(cls+"."+name,out var method)&&(string)method["returns"]=="void"&&((JArray)method["parameters"]).Count==0,"callback-signature");
   }
   foreach(string name in new[]{"Start","Update"})if(methods.ContainsKey(cls+"."+name))Need(seen.Contains(name),"unregistered-callback");
  }
 }
}

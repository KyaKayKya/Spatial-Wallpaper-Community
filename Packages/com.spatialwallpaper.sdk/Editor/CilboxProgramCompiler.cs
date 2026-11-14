using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Cilbox;
using UnityEngine;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 public sealed class CompiledProgram {
  public JObject Program{get;internal set;}
  internal Dictionary<string,string> Classes=new Dictionary<string,string>(StringComparer.Ordinal);
  internal Dictionary<string,string> Fields;
  internal Dictionary<string,Type> SourceTypes;
  internal Func<Type,string> MapType;
  public string ClassId(Type type)=>Classes[type.FullName];
  public string FieldId(Type type,string name)=>Fields[type.FullName+"."+name];
 }
 public static class CilboxProgramCompiler {
  static readonly Dictionary<ushort,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(c=>unchecked((ushort)c.Value));
  sealed class Converter {
   readonly SerializedAssembly assembly;readonly CompiledProgram result=new CompiledProgram();readonly Dictionary<string,Type> originals;
   readonly Dictionary<string,string> fields=new Dictionary<string,string>(StringComparer.Ordinal),methods=new Dictionary<string,string>(StringComparer.Ordinal);
   readonly Dictionary<string,string> methodReturns=new Dictionary<string,string>(StringComparer.Ordinal);
   readonly Dictionary<int,SerializedMetadataToken> metadata;
   readonly RuleCatalog rules;
   public Converter(SerializedAssembly source,Dictionary<string,Type> native,RuleCatalog rules){assembly=source;originals=native;this.rules=rules;metadata=source.metadata.ToDictionary(m=>m.metaTokenIndex);}
   void Need(bool yes,string code){if(!yes)throw new InvalidDataException("csharp-subset:"+code);}
   string TypeId(SerializedTypeDescriptor type){
    Need(type!=null&&!string.IsNullOrEmpty(type.typeName),"type-descriptor");
    if(!string.IsNullOrEmpty(type.genericName)){
     Need(type.assemblyName=="mscorlib"&&type.genericName==typeof(List<>).FullName&&type.genericArgs!=null&&type.genericArgs.Length==1,"generic-type");
     string element=TypeId(type.genericArgs[0]);Need(element=="i32"||element=="f32","generic-element");return "list_"+element;
    }
    if(result.Classes.TryGetValue(type.typeName,out var cls)){Need(originals[type.typeName].Assembly.GetName().Name==type.assemblyName,"author-assembly");return cls;}
    if(type.typeName.EndsWith("[]",StringComparison.Ordinal))return TypeId(new SerializedTypeDescriptor{assemblyName=type.assemblyName,typeName=type.typeName.Substring(0,type.typeName.Length-2)})+"[]";
    string key=type.assemblyName+"::"+type.typeName;
    switch(key){
     case "SpatialWallpaper.Sdk::SpatialWallpaper.Sdk.Prefab":return "prefab";
     case "UnityEngine.AudioModule::UnityEngine.AudioClip":return "sound";
     case "mscorlib::System.Void":return "void";case "mscorlib::System.Int32":return "i32";case "mscorlib::System.Single":return "f32";case "mscorlib::System.Boolean":return "bool";case "mscorlib::System.String":return "string";
     case "UnityEngine.CoreModule::UnityEngine.Transform":case "UnityEngine.CoreModule::UnityEngine.GameObject":return "node";
     case "UnityEngine.CoreModule::UnityEngine.Material":return "material";case "UnityEngine.CoreModule::UnityEngine.Mesh":return "mesh";
     case "UnityEngine.CoreModule::UnityEngine.Texture2D":return "texture";case "UnityEngine.CoreModule::UnityEngine.Cubemap":return "cubemap";
     case "UnityEngine.CoreModule::UnityEngine.Light":return "light";case "UnityEngine.CoreModule::UnityEngine.Vector3":return "vector3";case "UnityEngine.CoreModule::UnityEngine.Color":return "color";
     default:throw new InvalidDataException("csharp-subset:type:"+key);
    }
   }
   string TypeId(Type type)=>TypeId(SerializedTypeDescriptorBuilder.FromNativeType(type));
   static string Key(string cls,string signature)=>cls+"::"+signature;
   public CompiledProgram Convert(){
    var list=new JArray();int ci=0;
    foreach(var cls in assembly.classes){
     Need(originals.ContainsKey(cls.className),"unknown-class");var t=originals[cls.className];
     Need(!t.IsGenericType&&(t.BaseType==typeof(MonoBehaviour)||t.BaseType==typeof(object)),"inheritance");result.Classes.Add(cls.className,"c"+ci++);
    }
    foreach(var cls in assembly.classes){
     Need(cls.staticFields.All(f=>originals[cls.className].GetField(f.name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.IsLiteral==true),"static-state");
     var nativeType=originals[cls.className];
     if(typeof(MonoBehaviour).IsAssignableFrom(nativeType)){
      Need(!nativeType.GetInterfaces().Contains(typeof(ISerializationCallbackReceiver)),"serialization-callback");
      foreach(var method in cls.methods){
       bool message=((JArray)RuleCatalog.Current.Snapshot["unity_message_names"]).Any(n=>(string)n==method.methodName);
       Need(!message||((JArray)RuleCatalog.Current.Snapshot["callbacks"]).Any(n=>(string)n==method.methodName),"callback:"+method.methodName);
      }
     }
     int fi=0,mi=0;foreach(var f in cls.instanceFields)fields.Add(cls.className+"."+f.name,"f"+fi++);
     foreach(var m in cls.methods){Need(!m.isStatic,"static-method");string id=m.methodName=="Start"||m.methodName=="Update"?m.methodName:"m"+mi++;string key=Key(cls.className,m.fullSignature);methods.Add(key,result.Classes[cls.className]+"."+id);var native=nativeType.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).SingleOrDefault(x=>x.ToString()==m.fullSignature);methodReturns.Add(key,m.isVoid?"void":TypeId(native?.ReturnType??throw new InvalidDataException("return-type")));}
    }
    foreach(var cls in assembly.classes){
     var fs=new JArray();foreach(var f in cls.instanceFields)fs.Add(new JObject{{"id",fields[cls.className+"."+f.name]},{"type",TypeId(f.type)}});
     var ms=new JArray();foreach(var m in cls.methods){
      Need(m.exceptionHandlers==null||m.exceptionHandlers.Length==0,"exception-handlers");
      var native=originals[cls.className].GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).SingleOrDefault(x=>x.ToString()==m.fullSignature);
      string returns=m.isVoid?"void":TypeId(native?.ReturnType??throw new InvalidDataException("return-type"));
      var locals=new JArray(m.locals.Select(l=>TypeId(l.type)));
      ms.Add(new JObject{{"id",methods[Key(cls.className,m.fullSignature)].Split('.')[1]},{"returns",returns},{"parameters",new JArray(m.parameters.Select(a=>TypeId(a.type)))},{"locals",locals},{"body",Body(cls,m,locals,returns)}});
     }
     list.Add(new JObject{{"id",result.Classes[cls.className]},{"fields",fs},{"methods",ms}});
    }
    result.Program=new JObject{{"classes",list}};result.Fields=fields;result.SourceTypes=originals;result.MapType=TypeId;return result;
   }
   static JObject Ins(string op,object value=null)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
   SerializedMetadataToken Meta(int token){Need(metadata.ContainsKey(token),"metadata-token");return metadata[token];}
   string Native(SerializedMetadataToken meta){
    Need(meta.methodGenericArguments==null||meta.methodGenericArguments.Length==0,"generic-method");
    string prefix=meta.typeDescriptor.assemblyName+"::"+meta.typeDescriptor.typeName+"::"+meta.name+"("+string.Join(",",(meta.methodParameters??new SerializedTypeDescriptor[0]).Select(t=>t.typeName))+")->";
    if(!string.IsNullOrEmpty(meta.typeDescriptor.genericName)){
     string list=TypeId(meta.typeDescriptor),element=RuleCatalog.Current.ListElement(list);
     Need(element!=null,"generic-member");
     prefix="mscorlib::System.Collections.Generic.List<"+(element=="i32"?"System.Int32":"System.Single")+">::"+meta.name+"("+string.Join(",",(meta.methodParameters??new SerializedTypeDescriptor[0]).Select(t=>t.typeName))+")->";
    }
    var memberList=((JArray)rules.Snapshot["members"]).Cast<JObject>();
    var normalizedArguments=(meta.methodParameters??new SerializedTypeDescriptor[0]).Select(TypeId).ToArray();
    bool facade=meta.typeDescriptor.assemblyName=="SpatialWallpaper.Sdk"&&(meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Objects"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Resources"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Storage"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Llm"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Audio"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Input"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.View"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.Sound");
    facade=facade||meta.typeDescriptor.assemblyName=="SpatialWallpaper.Sdk"&&(meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.SceneAnimation"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.SceneQueries"||meta.typeDescriptor.typeName=="SpatialWallpaper.Sdk.SceneResources");
    if(facade)prefix="SpatialWallpaper.Sdk::"+meta.typeDescriptor.typeName.Substring("SpatialWallpaper.Sdk.".Length)+"::"+meta.name+"(";
    var matches=memberList.Where(r=>((string)r["id"]).StartsWith(prefix,StringComparison.Ordinal)&&((JArray)r["arguments"]).Select(x=>(string)x).SequenceEqual(normalizedArguments)).ToArray();
    Need(matches.Length==1,"member:"+prefix);Need(((string)matches[0]["receiver"]=="")==meta.isStatic,"member-static");return (string)matches[0]["id"];
   }
   JArray Body(SerializedClass cls,SerializedMethod method,JArray locals,string returns){
    var output=new JArray();var positions=new Dictionary<int,int>();var branches=new List<Tuple<JObject,int>>();byte[] code=method.body;int pc=0;
    // CLI represents boolean evaluation-stack values as I4. Normalize only
    // known bool producers; arbitrary integer values must retain every bit.
    // Existing mixed bitwise semantics map bool to 0/1 without broadening
    // strict admission or using conv.i4, which accepts numeric values only.
    Action booleanStack=()=>{output.Add(Ins("ldc.i4",1));output.Add(Ins("and"));};
    Action<int> loadLocal=index=>{output.Add(Ins("ldloc",index));if((string)locals[index]=="bool")booleanStack();};
    Action<int> loadArgument=index=>{output.Add(Ins("ldarg",index));if(index>0){Need(index<=method.parameters.Length,"argument-index");if(TypeId(method.parameters[index-1].type)=="bool")booleanStack();}};
    Action<int> storeLocal=index=>{if((string)locals[index]=="bool")output.Add(Ins("conv.bool"));output.Add(Ins("stloc",index));};
    Action<SerializedMetadataToken> normalizeArguments=token=>{
     var types=(token.methodParameters??new SerializedTypeDescriptor[0]).Select(TypeId).ToArray();
     if(!types.Contains("bool"))return;
     var temporary=new int[types.Length];
     for(int i=types.Length-1;i>=0;i--){temporary[i]=locals.Count;locals.Add(types[i]);if(types[i]=="bool")output.Add(Ins("conv.bool"));output.Add(Ins("stloc",temporary[i]));}
     for(int i=0;i<types.Length;i++)output.Add(Ins("ldloc",temporary[i]));
    };
    Need(code!=null&&code.Length>0&&code.Length<=256*1024,"body-size");
    while(pc<code.Length){
     int start=pc;positions.Add(start,output.Count);ushort number=code[pc++];if(number==0xfe){Need(pc<code.Length,"opcode-tail");number=(ushort)(0xfe00|code[pc++]);}
     Need(Codes.TryGetValue(number,out var opcode),"opcode");int length;
     switch(opcode.OperandType){
      case OperandType.InlineNone:length=0;break;
      case OperandType.ShortInlineI:case OperandType.ShortInlineVar:case OperandType.ShortInlineBrTarget:length=1;break;
      case OperandType.InlineVar:length=2;break;
      case OperandType.ShortInlineR:case OperandType.InlineI:case OperandType.InlineBrTarget:case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineString:case OperandType.InlineType:length=4;break;
      default:throw new InvalidDataException("csharp-subset:operand:"+opcode.Name);
     }
     Need(length<=code.Length-pc,"operand-tail");int operand=length==1?code[pc]:length==2?BitConverter.ToUInt16(code,pc):length==4?BitConverter.ToInt32(code,pc):0;int end=pc+length;pc=end;string op=opcode.Name;
     if(op.StartsWith("ldarg.",StringComparison.Ordinal)&&op!="ldarg.s"){loadArgument(int.Parse(op.Substring(6)));continue;}
     if(op.StartsWith("ldloc.",StringComparison.Ordinal)&&op!="ldloc.s"){loadLocal(int.Parse(op.Substring(6)));continue;}
     if(op.StartsWith("stloc.",StringComparison.Ordinal)&&op!="stloc.s"){storeLocal(int.Parse(op.Substring(6)));continue;}
     if(op.StartsWith("ldc.i4.",StringComparison.Ordinal)&&op!="ldc.i4.s"){output.Add(Ins("ldc.i4",op=="ldc.i4.m1"?-1:int.Parse(op.Substring(7))));continue;}
     if(op=="stloc.s"||op=="stloc"){storeLocal(operand);continue;}
     if(op=="ldarg.s"||op=="ldarg"){loadArgument(operand);continue;}
     if(op=="ldloc.s"||op=="ldloc"){loadLocal(operand);continue;}
      // Only an immediate read of a value component may use a CIL address.
      // No managed pointer enters the normalized program or crosses a branch.
      if(op=="ldloca.s"||op=="ldloca"||op=="ldarga.s"||op=="ldarga"){
       bool local=op.StartsWith("ldloca",StringComparison.Ordinal);string valueType=local?(string)locals[operand]:operand==0?result.Classes[cls.className]:TypeId(method.parameters[operand-1].type);
       Need(valueType=="vector3"||valueType=="color","value-component-address-type");Need(pc+5<=code.Length&&code[pc]==0x7b,"value-component-read-required");
       var field=Meta(BitConverter.ToInt32(code,pc+1));string expected=valueType=="vector3"?"UnityEngine.Vector3":"UnityEngine.Color";
       int component=(valueType=="vector3"?"xyz":"rgba").IndexOf(field.name,StringComparison.Ordinal);
       Need(!field.isStatic&&field.typeDescriptor.assemblyName=="UnityEngine.CoreModule"&&field.typeDescriptor.typeName==expected&&field.name.Length==1&&component>=0,"value-component-field");
       output.Add(Ins(local?"ldloc":"ldarg",operand));output.Add(Ins("getcomponent",component));pc+=5;continue;
      }
     if(op=="ldc.i4.s"){output.Add(Ins("ldc.i4",(int)unchecked((sbyte)operand)));continue;}
     if(op=="ldc.r4"){output.Add(Ins(op,BitConverter.ToSingle(code,end-4)));continue;}
     if(op=="ldc.i4"){output.Add(Ins(op,operand));continue;}
     if(opcode.OperandType==OperandType.InlineBrTarget||opcode.OperandType==OperandType.ShortInlineBrTarget){
      int target=end+(length==1?unchecked((sbyte)operand):operand);string branch=op.EndsWith(".s",StringComparison.Ordinal)?op.Substring(0,op.Length-2):op;
      if(branch=="beq"||branch=="bne.un"){output.Add(Ins("ceq"));booleanStack();branch=branch=="beq"?"brtrue":"brfalse";}
      else if(new[]{"blt","bgt","ble","bge","blt.un","bgt.un","ble.un","bge.un"}.Contains(branch)){
       output.Add(Ins("c"+branch.Substring(1)));booleanStack();branch="brtrue";
      }
      Need(branch=="br"||branch=="brtrue"||branch=="brfalse","branch:"+op);var instruction=Ins(branch,0);branches.Add(Tuple.Create(instruction,target));output.Add(instruction);continue;
     }
     if(op=="ldfld"||op=="stfld"){
       var token=Meta(operand);
       if(op=="ldfld"&&token.typeDescriptor.assemblyName=="UnityEngine.CoreModule"&&(token.typeDescriptor.typeName=="UnityEngine.Vector3"||token.typeDescriptor.typeName=="UnityEngine.Color")){
        int component=(token.typeDescriptor.typeName=="UnityEngine.Vector3"?"xyz":"rgba").IndexOf(token.name,StringComparison.Ordinal);Need(!token.isStatic&&token.name.Length==1&&component>=0,"value-component-field");output.Add(Ins("getcomponent",component));continue;
       }
       Need(!token.isStatic&&fields.ContainsKey(token.typeDescriptor.typeName+"."+token.name),"field:"+cls.className+"."+method.methodName+":"+start+":"+token.typeDescriptor.typeName+"."+token.name);
      if(op=="stfld"&&originals[token.typeDescriptor.typeName].GetField(token.name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.FieldType==typeof(bool))output.Add(Ins("conv.bool"));
      output.Add(Ins(op=="ldfld"?"ldfield":"stfield",fields[token.typeDescriptor.typeName+"."+token.name]));if(op=="ldfld"&&originals[token.typeDescriptor.typeName].GetField(token.name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.FieldType==typeof(bool))booleanStack();continue;
     }
     if(op=="ldstr"){output.Add(Ins("ldstr",Meta(operand).stringValue));continue;}
     if(op=="newarr"||op=="ldelem"||op=="stelem"){string type=TypeId(Meta(operand).typeDescriptor);if(op=="stelem"&&type=="bool")output.Add(Ins("conv.bool"));output.Add(Ins(op=="newarr"?"newarray":op,type));if(op=="ldelem"&&type=="bool")booleanStack();continue;}
     if(op=="ldelem.i4"||op=="ldelem.r4"||op=="stelem.i4"||op=="stelem.r4"){output.Add(Ins(op.StartsWith("ld",StringComparison.Ordinal)?"ldelem":"stelem",op.EndsWith("i4",StringComparison.Ordinal)?"i32":"f32"));continue;}
      if(op=="ldelem.ref"||op=="stelem.ref"){output.Add(Ins(op));continue;}
      if(op=="ldelem.u1"){output.Add(Ins("ldelem","bool"));booleanStack();continue;}
      if(op=="stelem.i1"){output.Add(Ins("conv.bool"));output.Add(Ins("stelem","bool"));continue;}
     if(op=="call"||op=="callvirt"||op=="newobj"){
      var token=Meta(operand);string declaring=token.typeDescriptor.typeName;normalizeArguments(token);
      if(op=="call"&&declaring=="System.String"&&token.typeDescriptor.assemblyName=="mscorlib"&&(token.name=="op_Equality"||token.name=="op_Inequality")){
       Need(token.isStatic&&string.IsNullOrEmpty(token.typeDescriptor.genericName)&&(token.methodGenericArguments==null||token.methodGenericArguments.Length==0)&&token.methodParameters!=null&&token.methodParameters.Length==2&&token.methodParameters.All(t=>t.assemblyName=="mscorlib"&&t.typeName=="System.String"&&string.IsNullOrEmpty(t.genericName))&&token.methodFullSignature==typeof(string).GetMethod(token.name,new[]{typeof(string),typeof(string)}).ToString(),"string-operator-signature");
       // VM ceq uses string value equality, including null, not reference identity.
       output.Add(Ins("ceq"));if(token.name=="op_Inequality"){output.Add(Ins("ldc.i4",0));output.Add(Ins("ceq"));}booleanStack();continue;
      }
      if(token.name==".ctor"&&(declaring=="UnityEngine.MonoBehaviour"&&token.typeDescriptor.assemblyName=="UnityEngine.CoreModule"||declaring=="System.Object"&&token.typeDescriptor.assemblyName=="mscorlib")){
       Need(method.isCtor&&(token.methodParameters==null||token.methodParameters.Length==0),"base-constructor");output.Add(Ins("pop"));continue;
      }
      if(op=="newobj"&&!string.IsNullOrEmpty(token.typeDescriptor.genericName)){
       string type=TypeId(token.typeDescriptor);Need(RuleCatalog.Current.ListElement(type)!=null,"generic-constructor");
       if(token.methodParameters==null||token.methodParameters.Length==0)output.Add(Ins("ldc.i4",0));
       else Need(token.methodParameters.Length==1&&TypeId(token.methodParameters[0])=="i32","list-constructor");
       output.Add(Ins("newlist",type));continue;
      }
      if(op=="newobj"&&(declaring=="UnityEngine.Vector3"||declaring=="UnityEngine.Color")&&token.typeDescriptor.assemblyName=="UnityEngine.CoreModule"){
       Need(token.methodParameters.All(t=>TypeId(t)=="f32")&&token.methodParameters.Length==(declaring.EndsWith("Vector3",StringComparison.Ordinal)?3:4),"value-constructor");
       output.Add(Ins(declaring.EndsWith("Vector3",StringComparison.Ordinal)?"newvector3":"newcolor"));continue;
      }
      if(result.Classes.ContainsKey(declaring)){
       Need(originals[declaring].Assembly.GetName().Name==token.typeDescriptor.assemblyName,"method-assembly");string key=Key(declaring,token.methodFullSignature);Need(methods.ContainsKey(key),"author-method");
       if(op=="newobj"){Need(token.methodParameters==null||token.methodParameters.Length==0,"constructor-parameters");output.Add(Ins("newobj",result.Classes[declaring]));output.Add(Ins("dup"));}
       output.Add(Ins("invoke",methods[key]));if(methodReturns[key]=="bool")booleanStack();continue;
      }
      Need(op!="newobj","native-constructor");string member=Native(token);output.Add(Ins("call",member));if((string)rules.Member(member)["returns"]=="bool")booleanStack();continue;
     }
     if(new[]{"nop","ldnull","dup","pop","add","sub","mul","div","rem","neg","not","and","or","xor","ceq","clt","cgt","clt.un","cgt.un","conv.i4","conv.r4","ldlen","ret","ldarg","ldloc","stloc"}.Contains(op)){
      if(op=="ret"&&returns=="bool")output.Add(Ins("conv.bool"));
      output.Add(Ins(op,op=="ldarg"||op=="ldloc"||op=="stloc"?(object)operand:null));if(op=="ceq"||op=="clt"||op=="cgt"||op=="clt.un"||op=="cgt.un")booleanStack();continue;
     }
     throw new InvalidDataException("csharp-subset:opcode:"+cls.className+"."+method.methodName+":"+start+":"+op);
    }
    foreach(var branch in branches){Need(positions.ContainsKey(branch.Item2),"branch-target");branch.Item1["value"]=positions[branch.Item2];}
    return output;
   }
  }
  sealed class ReferenceComparer:IEqualityComparer<object>{
   public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
   public int GetHashCode(object value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
  }
  static MonoBehaviour[] DiscoverAuthors(GameObject root){
   var roots=new Queue<GameObject>();var visitedRoots=new HashSet<GameObject>();var authors=new List<MonoBehaviour>();roots.Enqueue(root);
   Action<object,int,HashSet<object>> visit=null;
   visit=(value,depth,seen)=>{
    if(value==null)return;if(depth>16)throw new InvalidDataException("author-reference-depth");
    if(value is Prefab prefab){if(!prefab.source||!UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab.source))throw new InvalidDataException("prefab-asset-required");roots.Enqueue(prefab.source);return;}
    if(!seen.Add(value))return;
    if(value is Array array){if(array.Rank!=1||array.Length>RuleCatalog.Current.Limit("resources"))throw new InvalidDataException("author-array-limit");foreach(var item in array)visit(item,depth+1,seen);return;}
    var type=value.GetType();if(!CilboxUtil.HasCilboxableAttribute(type))return;
    foreach(var field in CilboxUtil.GetInstanceFieldsBaseFirst(type))visit(field.GetValue(value),depth+1,seen);
   };
   while(roots.Count>0){
    var next=roots.Dequeue();if(!visitedRoots.Add(next))continue;
    if(visitedRoots.Count>RuleCatalog.Current.Limit("prefabs")+1)throw new InvalidDataException("prefab-reference-limit");
    foreach(var behaviour in next.GetComponentsInChildren<MonoBehaviour>(true).Where(m=>m&&CilboxUtil.HasCilboxableAttribute(m.GetType()))){authors.Add(behaviour);visit(behaviour,0,new HashSet<object>(new ReferenceComparer()));}
   }
   return authors.ToArray();
  }
  public static CompiledProgram CompileForNativeBridge(GameObject root){
   var result=Compile(root,SpatialWallpaper.Sdk.Native.NativeAuthorApi.Rules);
   SpatialWallpaper.Sdk.Native.NativeProgramBindings.Require(result.Program);
   return result;
  }
  public static CompiledProgram Compile(GameObject root)=>Compile(root,RuleCatalog.Current);
  static CompiledProgram Compile(GameObject root,RuleCatalog rules){
   if(!root||!root.scene.IsValid())throw new ArgumentException("author-root");
   var authors=DiscoverAuthors(root);
   if(authors.Length==0)return new CompiledProgram{Program=new JObject{{"classes",new JArray()}},Fields=new Dictionary<string,string>(),SourceTypes=new Dictionary<string,Type>(),MapType=type=>throw new InvalidDataException("no-author-field-type")};
   var originals=new Dictionary<string,Type>(StringComparer.Ordinal);
   foreach(var assembly in authors.Select(a=>a.GetType().Assembly).Distinct())foreach(var type in assembly.GetTypes().Where(CilboxUtil.HasCilboxableAttribute)){
    if(originals.ContainsKey(type.FullName))throw new InvalidDataException("ambiguous-author-type");originals.Add(type.FullName,type);
   }
   CilboxScenePostprocessor.OnPostprocessScene(root.scene,true,authors.Select(a=>a.GetType()).Distinct().ToArray());
   if(CilboxScenePostprocessor.LastCompiledAssembly==null)throw new InvalidDataException("compiler-produced-no-program");
   return new Converter(CilboxScenePostprocessor.LastCompiledAssembly,originals,rules).Convert();
  }
 }
}

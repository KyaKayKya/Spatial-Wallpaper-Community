using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;
using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Capabilities;
static class CapabilityRuleChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static bool Scope(string id)=>id.StartsWith("SpatialWallpaper.Sdk::Storage::",StringComparison.Ordinal)||id.StartsWith("SpatialWallpaper.Sdk::Llm::",StringComparison.Ordinal);
 static string Signature(Type type,MethodInfo method)=>"SpatialWallpaper.Sdk::"+type.Name+"::"+method.Name+"("+string.Join(",",method.GetParameters().Select(p=>p.ParameterType.FullName))+")->"+method.ReturnType.FullName;
 static int Main(string[] args){try{
  var rules=new HashSet<string>(((JArray)RuleCatalog.Current.Snapshot["members"]).Select(m=>(string)m["id"]).Where(Scope),StringComparer.Ordinal);
  var facade=new HashSet<string>(StringComparer.Ordinal);var results=new JArray();
  foreach(var type in new[]{typeof(SpatialWallpaper.Sdk.Storage),typeof(SpatialWallpaper.Sdk.Llm)})
   foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly)){
    Need(!method.IsGenericMethodDefinition,"generic-capability-facade");facade.Add(Signature(type,method));
   }
  Need(rules.SetEquals(facade),"rule-facade-signature-set-drift");
  var bound=(IEnumerable<string>)typeof(CapabilityBindings).GetField("Members",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  Need(rules.SetEquals(bound.Where(Scope)),"rule-worker-binding-signature-set-drift");
  foreach(var id in rules.OrderBy(x=>x,StringComparer.Ordinal)){
   Need(CapabilityBindings.Supports(id),"rule-not-worker-bound:"+id);
   results.Add(new JObject{{"member",id},{"facade",true},{"worker_binding",true}});
  }
  var rejected=new JArray();
  foreach(var id in new[]{"SpatialWallpaper.Sdk::Storage::Read(System.String)->System.String","SpatialWallpaper.Sdk::Storage::Write(System.String,System.String)->System.Void","SpatialWallpaper.Sdk::Llm::Submit(System.String)->System.Int32","SpatialWallpaper.Sdk::Llm::BeginPrompt(System.Int32)->System.Int32"}){
   Need(!CapabilityBindings.Supports(id),"unsupported-member-worker-bound");bool denied=false;
   try{RuleCatalog.Current.Member(id);}catch(InvalidDataException error){denied=error.Message.StartsWith("member-not-allowed:",StringComparison.Ordinal);}
   Need(denied,"unsupported-member-rule-admitted:"+id);rejected.Add(id);
  }
  File.WriteAllText(args[0],new JObject{{"rules_digest",RuleCatalog.Current.Digest},{"members",results},{"rejected",rejected},{"scope","compiled-static-facade-and-worker-binding-not-Unity-or-runtime-acceptance"}}.ToString());
  Console.WriteLine("PASS exact Storage/Llm facade, rule and Worker binding sets; retired and unknown signatures rejected");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}

using System;
using System.Collections.Generic;
namespace SpatialWallpaper.Sdk.Execution {
 internal static class CoreLibrary {
  static readonly Dictionary<string,string> Operations=new Dictionary<string,string>(StringComparer.Ordinal);
  static CoreLibrary(){
   foreach(string native in new[]{"System.Int32","System.Single"}){
    string prefix="mscorlib::System.Collections.Generic.List<"+native+">::";
    Operations.Add(prefix+"Add("+native+")->System.Void","add");
    Operations.Add(prefix+"get_Count()->System.Int32","count");
    Operations.Add(prefix+"get_Item(System.Int32)->"+native,"get");
    Operations.Add(prefix+"set_Item(System.Int32,"+native+")->System.Void","set");
    Operations.Add(prefix+"Clear()->System.Void","clear");
   }
  }
  public static bool Supports(string member)=>Operations.ContainsKey(member);
  public static object Call(OwnedDomain domain,string member,object receiver,object[] args){
   var list=(OwnedList)receiver;
   switch(Operations[member]){
    case "add":domain.Add(list,args[0]);return null;case "count":domain.Check(list);return list.Count;
    case "get":return domain.Get(list,(int)args[0]);case "set":domain.Set(list,(int)args[0],args[1]);return null;
    case "clear":domain.Clear(list);return null;default:throw new InvalidOperationException("core-member");
   }
  }
 }
}

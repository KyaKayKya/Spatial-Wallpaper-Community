// Compatibility experiment only. Production admission/ownership is not implemented here.
using System;
using System.Reflection;
using Cilbox;
using UnityEngine;
public sealed class StageABox:Cilbox.Cilbox {
 public override bool CheckMethodAllowed(out MethodInfo method,Type type,string name,SerializedTypeDescriptor[] parameters,SerializedTypeDescriptor[] generics,string signature){
  method=null;
  return (type==typeof(Transform)&&name=="Rotate"&&parameters.Length==3&&(generics==null||generics.Length==0))
   || (type==typeof(object)&&name==".ctor"&&parameters.Length==0)
   || (type==typeof(MonoBehaviour)&&name==".ctor"&&parameters.Length==0);
 }
 public override bool CheckTypeAllowed(Type t){return t==typeof(int)||t==typeof(float)||t==typeof(void)||t==typeof(object)||t==typeof(Transform)||t==typeof(MonoBehaviour)||t==typeof(CilboxProxy);}
 public override bool CheckFieldAllowed(Type t,string name){return false;}
 public override bool GetTypeOverride(string name,out Type type){type=null;return false;}
}

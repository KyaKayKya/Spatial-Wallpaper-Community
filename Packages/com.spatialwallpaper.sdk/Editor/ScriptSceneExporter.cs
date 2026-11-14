using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 public static class ScriptSceneExporter {
  public static byte[] Export(GameObject root,Vector3 frame,JArray licenses){
   var program=CilboxProgramCompiler.Compile(root);var exporter=new Exporter(program,root);
   var content=exporter.Content(frame);
   return PackageWriter.Export(program.Program,content,exporter.Resources.Files,licenses);
  }
  internal static JObject EncodePresentation(ScenePresentationSettings settings){
   if(!settings.enabled||settings.customCamera||settings.customClipDepth)throw new InvalidDataException("export:presentation-camera-or-clip-override-not-supported");
   if(settings.customAmbient&&settings.customAmbientGradient)throw new InvalidDataException("export:duplicate-author-ambient");
   JArray Color(UnityEngine.Color value)=>new JArray(value.r,value.g,value.b,value.a);
   var config=new JObject{{"ambient",settings.customAmbient},{"gradient",settings.customAmbientGradient},{"sky",Color(settings.customAmbient?settings.ambientIrradiance:settings.ambientSky)},{"equator",Color(settings.ambientEquator)},{"ground",Color(settings.ambientGround)},{"background",settings.customBackground},{"backgroundColor",Color(settings.backgroundColor)},{"post",settings.postProcessing},{"threshold",settings.bloom.x},{"intensity",settings.bloom.y},{"scatter",settings.bloom.z},{"aces",settings.aces},{"exposure",settings.exposure},{"saturation",settings.saturation},{"vignette",settings.vignette.x},{"smoothness",settings.vignette.y}};
   var schema=(JObject)RuleCatalog.Current.Snapshot["components"]["presentation"];
   foreach(var field in schema.Properties())ValueContract.Check(config[field.Name],(JObject)field.Value,(type,value)=>{throw new InvalidDataException("presentation-reference-not-supported");});
   return config;
  }
  sealed class Exporter {
   readonly CompiledProgram program;readonly List<Transform> transforms=new List<Transform>();
   readonly Dictionary<Transform,int> indices=new Dictionary<Transform,int>();
   readonly Dictionary<GameObject,string> prefabIds;readonly List<GameObject> prefabSources;
   internal readonly SceneResourceRegistry Resources;

   public Exporter(CompiledProgram compiled,GameObject root,Dictionary<GameObject,string> ids=null,List<GameObject> sources=null,SceneResourceRegistry resources=null){program=compiled;Resources=resources??new SceneResourceRegistry();prefabIds=ids??new Dictionary<GameObject,string>();prefabSources=sources??new List<GameObject>();Collect(root.transform);}
   void Need(bool yes,string message){if(!yes)throw new InvalidDataException("export:"+message);}
   void Collect(Transform transform){
    var pending=new Stack<Transform>();pending.Push(transform);
    while(pending.Count>0){var item=pending.Pop();Need(transforms.Count<RuleCatalog.Current.Limit("nodes"),"node-limit");indices.Add(item,transforms.Count);transforms.Add(item);for(int i=item.childCount-1;i>=0;i--)pending.Push(item.GetChild(i));}
   }
   string PrefabId(Prefab value){
    Need(value!=null&&value.source&&PrefabUtility.IsPartOfPrefabAsset(value.source),"prefab-asset-required");
    if(!prefabIds.TryGetValue(value.source,out var id)){Need(prefabSources.Count<RuleCatalog.Current.Limit("prefabs"),"prefab-limit");id="p"+prefabSources.Count;prefabIds.Add(value.source,id);prefabSources.Add(value.source);}return id;
   }
   JObject Fields(Type type,object instance,int depth){
    Need(depth<=16&&instance!=null&&instance.GetType()==type,"data-shape");
    var cls=((JArray)program.Program["classes"]).Cast<JObject>().Single(c=>(string)c["id"]==program.ClassId(type));var result=new JObject();
    var fields=Cilbox.CilboxUtil.GetInstanceFieldsBaseFirst(type);
    foreach(JObject field in (JArray)cls["fields"]){
     string id=(string)field["id"];
     var native=fields.Single(f=>program.FieldId(type,f.Name)==id);
     result[id]=Value(native.FieldType,native.GetValue(instance),depth+1);
    }
    return result;
   }
   JToken Value(Type type,object value,int depth){
    Need(depth<=16,"field-depth");if(value==null||value is UnityEngine.Object unity&&!unity)return JValue.CreateNull();
    string id=program.MapType(type);
    if(type.IsArray){var array=(Array)value;Need(value.GetType()==type&&array.Rank==1&&array.Length<=(int)RuleCatalog.Current.Constraint(id)["max_elements"],"array-shape");return new JArray(array.Cast<object>().Select(item=>Value(type.GetElementType(),item,depth+1)));}
    string listElement=RuleCatalog.Current.ListElement(id);
    if(listElement!=null){Need(value.GetType()==type,"list-shape");var list=(IList)value;Need(list.Count<=(int)RuleCatalog.Current.Constraint(id)["max_elements"],"list-count");return new JArray(list.Cast<object>().Select(item=>Value(type.GenericTypeArguments[0],item,depth+1)));}
    switch(id){
     case "sound":case "mesh":case "material":case "texture":case "cubemap":return new JValue(Resources.Reference((UnityEngine.Object)value));
     case "prefab":return new JValue(PrefabId((Prefab)value));
     case "i32":return new JValue((int)value);case "f32":return new JValue((float)value);case "bool":return new JValue((bool)value);case "string":return new JValue((string)value);
     case "vector3":return Vector((Vector3)value);case "color":var color=(Color)value;return new JArray(color.r,color.g,color.b,color.a);
     case "node":
      Transform target=value is Transform t?t:((GameObject)value).transform;Need(indices.ContainsKey(target),"foreign-node-reference");return new JValue(indices[target]);
     case "light":
      var light=(Light)value;Need(indices.ContainsKey(light.transform),"foreign-light-reference");return new JValue(indices[light.transform]);
    }
    if(typeof(MonoBehaviour).IsAssignableFrom(type)){
     var behaviour=(MonoBehaviour)value;Need(indices.ContainsKey(behaviour.transform)&&behaviour.GetType()==type,"foreign-script-reference");
     return new JObject{{"script",indices[behaviour.transform]}};
    }
    Need(program.Classes.ContainsKey(type.FullName),"resource-type-not-yet-exportable");
    return new JObject{{"fields",Fields(type,value,depth+1)}};
   }
   JArray Nodes(){
    var nodes=new JArray();
    foreach(var transform in transforms){
     Need(transform.gameObject.activeSelf,"inactive-node-not-supported");
     Need(transform.gameObject.layer==0,"node-layer-not-supported");
     var components=new JArray(new JObject{{"kind","transform"},{"config",new JObject{{"position",Vector(transform.localPosition)},{"rotation",new JArray(transform.localRotation.x,transform.localRotation.y,transform.localRotation.z,transform.localRotation.w)},{"scale",Vector(transform.localScale)}}},{"fields",new JObject()},{"callbacks",new JArray()}});
     int scripts=0;
     var filter=transform.GetComponent<MeshFilter>();var renderer=transform.GetComponent<MeshRenderer>();
     if(filter||renderer)components.Add(Resources.Renderer(filter,renderer));
     foreach(var component in transform.GetComponents<Component>()){
      Need(component!=null,"missing-script");if(component.GetType()==typeof(Transform))continue;
      if(component.GetType()==typeof(MeshFilter)||component.GetType()==typeof(MeshRenderer))continue;
      if(component.GetType()==typeof(Light)){
       components.Add(new JObject{{"kind","light"},{"config",SceneLightEncoding.Encode((Light)component)},{"fields",new JObject()},{"callbacks",new JArray()}});continue;
      }
      if(component.GetType()==typeof(ScenePresentationSettings)){
       var settings=(ScenePresentationSettings)component;
       Need(transform==transforms[0]&&settings.enabled,"presentation-root-and-enabled-required");
       components.Add(new JObject{{"kind","presentation"},{"config",EncodePresentation(settings)},{"fields",new JObject()},{"callbacks",new JArray()}});continue;
      }
      if(component.GetType()==typeof(UnityEngine.Rendering.Universal.UniversalAdditionalLightData)){
       Need(transform.GetComponent<Light>(),"light-additional-data-without-light");continue;
      }
      if(component is SceneDefinition&&transform==transforms[0])continue;
      if(component is MonoBehaviour script&&program.Classes.ContainsKey(script.GetType().FullName)){
       Need(script.enabled&&++scripts==1,"script-enabled-or-count");
       var cls=((JArray)program.Program["classes"]).Cast<JObject>().Single(c=>(string)c["id"]==program.ClassId(script.GetType()));
       var callbacks=new JArray(((JArray)cls["methods"]).Where(m=>(string)m["id"]=="Start"||(string)m["id"]=="Update").Select(m=>(string)m["id"]));
       components.Add(new JObject{{"kind","script"},{"config",new JObject{{"class",program.ClassId(script.GetType())}}},{"fields",Fields(script.GetType(),script,0)},{"callbacks",callbacks}});
      }else throw new InvalidDataException("export:component-not-yet-exportable:"+component.GetType().FullName);
     }
     nodes.Add(new JObject{{"id","n"+indices[transform]},{"parent",transform==transforms[0]?-1:indices[transform.parent]},{"components",components}});
    }
    return nodes;
   }
   public JObject Content(Vector3 frame){
    var nodes=Nodes();var prefabs=new JArray();
    for(int i=0;i<prefabSources.Count;i++)prefabs.Add(new JObject{{"id","p"+i},{"nodes",new Exporter(program,prefabSources[i],prefabIds,prefabSources,Resources).Nodes()}});
    return new JObject{{"frame",Vector(frame)},{"resources",Resources.Definitions},{"nodes",nodes},{"prefabs",prefabs}};
   }
   static JArray Vector(Vector3 value)=>new JArray(value.x,value.y,value.z);
  }
 }
}

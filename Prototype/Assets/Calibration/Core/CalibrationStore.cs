using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SpatialTracking {
 public interface ICalibrationStore {
  CalibrationProfile[] List();
  CalibrationProfile Save(CalibrationProfile draft,string name,bool saveAs);
 }
 public sealed class CalibrationStore:ICalibrationStore {
  readonly string root;
  public CalibrationStore(string directory){root=Path.GetFullPath(directory);Directory.CreateDirectory(root);}
  public CalibrationProfile[] List(){
   var result=new List<CalibrationProfile>();
   foreach(string file in Directory.GetFiles(root,"*.json")){
    var data=TrackingWire.Decode(File.ReadAllBytes(file));
    if(data["schema_version"]?.Type!=Newtonsoft.Json.Linq.JTokenType.Integer||data["id"]?.Type!=Newtonsoft.Json.Linq.JTokenType.String||data["revision"]?.Type!=Newtonsoft.Json.Linq.JTokenType.Integer)throw new InvalidDataException("INVALID_PRESET: "+Path.GetFileName(file));
    var p=data.ToObject<CalibrationProfile>();
    if(p==null||!p.Valid())throw new InvalidDataException("INVALID_PRESET: "+Path.GetFileName(file));
    result.Add(p);
   }return result.OrderBy(p=>p.name,StringComparer.OrdinalIgnoreCase).ToArray();
  }
  public CalibrationProfile Save(CalibrationProfile draft,string name,bool saveAs){
   if(draft==null||!draft.Valid()||(draft.schema_version==2?!draft.manual.Bound:!draft.reference_ready))throw new InvalidDataException("INVALID_PRESET");
   name=(name??"").Trim();if(name.Length==0||name.Length>48||name.Any(char.IsControl))throw new InvalidDataException("INVALID_NAME");
   var p=draft.Copy();if(saveAs){p.id=Guid.NewGuid().ToString("N");p.revision=0;}
   if(!Guid.TryParseExact(p.id,"N",out var id))throw new InvalidDataException("INVALID_ID");
   var all=List();if(all.Any(x=>x.id!=p.id&&string.Equals(x.name,name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("DUPLICATE_NAME");
   var previous=all.FirstOrDefault(x=>x.id==p.id);if(previous!=null&&previous.revision!=p.revision)throw new InvalidDataException("REVISION_CONFLICT");
   p.name=name;p.revision++;string path=Path.Combine(root,p.id+".json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temp,JsonConvert.SerializeObject(p,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally{if(File.Exists(temp))File.Delete(temp);}
   return p;
  }
 }
}

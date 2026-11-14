using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Content;

static class ContentChecks {
 static int passed,failed;
 static string output;
 static void Check(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
 static void Assert(bool v){if(!v)throw new Exception("assertion failed");}
 static void Reject(Action action,string reason=null){try{action();}catch(InvalidDataException e){if(reason!=null&&!e.Message.Contains(reason))throw new Exception("wrong rejection: "+e.Message);return;}throw new Exception("unsafe input accepted");}
 static void Zip(string name,Action<ZipArchive> write){using(var s=File.Create(Path.Combine(output,name)))using(var z=new ZipArchive(s,ZipArchiveMode.Create))write(z);}
 static void Entry(ZipArchive z,string name,string text){using(var w=new StreamWriter(z.CreateEntry(name).Open(),new UTF8Encoding(false)))w.Write(text);}
 static void Mutate(string original,string name,Action<JObject,Dictionary<string,byte[]>> change,bool rehash=true){
  var files=new Dictionary<string,byte[]>();using(var z=ZipFile.OpenRead(original))foreach(var e in z.Entries)using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);files.Add(e.FullName,m.ToArray());}
  var manifest=JsonData.Parse(Encoding.UTF8.GetString(files["manifest.json"]));change(manifest,files);
   if(name=="attack-mesh-invalid-index.zip"||name=="attack-texture-stream-config.zip"||name=="attack-instance-short-materials.zip"){
   var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));
   foreach(string group in new[]{"meshes","textures"})foreach(var item in ((JArray)scene[group]).ToArray()){
    string old=(string)item;string replacement=group+"/"+PackageValidator.Digest(files[old])+(group=="meshes"?".smesh":".stex");
    if(old!=replacement){files[replacement]=files[old];files.Remove(old);item.Replace(replacement);}
   }
   files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
  }
  if(rehash){manifest["files"]=new JObject(files.Where(p=>p.Key!="manifest.json").Select(p=>new JProperty(p.Key,new JObject{["size"]=p.Value.Length,["sha256"]=PackageValidator.Digest(p.Value)})));manifest["build_id"]=PackageValidator.Identity(manifest);}
  files["manifest.json"]=Encoding.UTF8.GetBytes(manifest.ToString());
  Zip(name,z=>{foreach(var p in files)using(var s=z.CreateEntry(p.Key).Open())s.Write(p.Value,0,p.Value.Length);});
 }
 static void Main(string[] args){
  output=args[0];Directory.CreateDirectory(output);
  Check("frame-keep-height-wide",()=>Assert(Framing.Scale(10,6,12,6,true)==1));
  Check("frame-keep-height-narrow",()=>Assert(Framing.Scale(10,6,8,6,true)==1));
  Check("frame-keep-width-wide",()=>Assert(Framing.Scale(10,6,12,6,false)==1.2));
  Check("frame-keep-width-narrow",()=>Assert(Framing.Scale(10,6,8,6,false)==.8));
  Check("frame-invalid",()=>Reject(()=>Framing.Scale(0,6,12,6,true)));
  foreach(string path in new[]{"../escape","/absolute","C:/bad","a\\b","A/../b","a//b","con","a/nul.txt","a/b.","a/b ","a:b","a/%2e","a\u0001b"}){
   string captured=path;Check("reject-path-"+captured,()=>Reject(()=>PackageValidator.CheckPath(captured)));
  }
  Check("safe-path",()=>PackageValidator.CheckPath("textures/abc-01.stex"));
  Check("reject-duplicate-json",()=>Reject(()=>JsonData.Parse("{\"a\":1,\"a\":2}")));
  Check("reject-non-finite",()=>Reject(()=>JsonData.Parse("{\"a\":NaN}")));
  Check("reject-type-activation",()=>Reject(()=>SceneData.Validate(JsonData.Parse("{\"$type\":\"ProbeCallback\"}"))));
  Check("reject-case-alias",()=>{Zip("alias.zip",z=>{Entry(z,"a","x");Entry(z,"A","y");});Reject(()=>PackageValidator.Validate(Path.Combine(output,"alias.zip")));});
  Check("reject-unlisted-file",()=>{Zip("extra.zip",z=>Entry(z,"malicious.dll","x"));Reject(()=>PackageValidator.Validate(Path.Combine(output,"extra.zip")));});
  Check("reject-link",()=>{Zip("link.zip",z=>{Entry(z,"manifest.json","{}");var e=z.CreateEntry("link");e.ExternalAttributes=unchecked((int)0xA1FF0000);});Reject(()=>PackageValidator.Validate(Path.Combine(output,"link.zip")),"package-link-or-directory");});
  Check("reject-entry-count-limit",()=>{Zip("entries.zip",z=>{for(int i=0;i<8193;i++)Entry(z,"entry-"+i,"x");});Reject(()=>PackageValidator.Validate(Path.Combine(output,"entries.zip")),"package-entry-count");});
  var shader=Path.GetFullPath(Path.Combine(output,"../../../resource-probe/bundles/shaders"));
  if(File.Exists(shader)){
   Check("external-shader-payload",()=>ShaderBundleGate.Validate(File.ReadAllBytes(shader)));
   Check("external-shader-truncated",()=>Reject(()=>ShaderBundleGate.Validate(new byte[128])));
  }
  if(args.Length>1){
   ShaderProgramChecks.Run(Path.Combine(output,"shader-attacks/program-0-0-0.bin"),Check,Assert);
   foreach(var attack in Directory.GetFiles(Path.Combine(output,"shader-attacks"),"*.shaderbundle"))Check("reject-native-config-"+Path.GetFileNameWithoutExtension(attack),()=>Reject(()=>ShaderBundleGate.Validate(File.ReadAllBytes(attack))));
   var package=PackageValidator.Validate(args[1]);
   Check("valid-exported-package",()=>Assert(package.Scene!=null&&package.BuildId.Length==64));
   Check("sample-floor-front-coplanar-with-screen",()=>{var scene=package.Scene;var floor=((JArray)scene["nodes"]).OfType<JObject>().Single(n=>(string)n["name"]=="Floor");var mesh=package.Read((string)scene["meshes"][(int)floor["mesh"]]);int n=BitConverter.ToInt32(mesh,4);float min=float.PositiveInfinity;for(int i=0;i<n;i++)min=Math.Min(min,BitConverter.ToSingle(mesh,12+i*72+8));Assert(Math.Abs((double)floor["position"][2]+(double)floor["scale"][2]*min)<1e-7);});
   Check("reject-declared-expansion-limit",()=>{var bytes=File.ReadAllBytes(args[1]);int eocd=bytes.Length-22;Assert(BitConverter.ToUInt32(bytes,eocd)==0x06054b50);int central=(int)BitConverter.ToUInt32(bytes,eocd+16);Assert(BitConverter.ToUInt32(bytes,central)==0x02014b50);Array.Copy(BitConverter.GetBytes(PackageValidator.MaxFile+1),0,bytes,central+24,4);File.WriteAllBytes(Path.Combine(output,"expansion.zip"),bytes);Reject(()=>PackageValidator.Validate(Path.Combine(output,"expansion.zip")),"package-size");});
   Check("reject-declared-texture-payload-limit",()=>{var bytes=File.ReadAllBytes(args[1]);int central=(int)BitConverter.ToUInt32(bytes,bytes.Length-22+16);bool found=false;while(BitConverter.ToUInt32(bytes,central)==0x02014b50){int names=BitConverter.ToUInt16(bytes,central+28),extra=BitConverter.ToUInt16(bytes,central+30),comment=BitConverter.ToUInt16(bytes,central+32);string name=Encoding.UTF8.GetString(bytes,central+46,names);if(name.EndsWith(".stex",StringComparison.Ordinal)){Array.Copy(BitConverter.GetBytes(128*1024*1024+45),0,bytes,central+24,4);found=true;break;}central+=46+names+extra+comment;}Assert(found);string path=Path.Combine(output,"texture-expansion.zip");File.WriteAllBytes(path,bytes);Reject(()=>PackageValidator.Validate(path),"package-size");});
   Check("reject-actual-unlisted-file",()=>{Mutate(args[1],"unlisted.zip",(m,f)=>f["extra.json"]=Encoding.UTF8.GetBytes("{}"),false);Reject(()=>PackageValidator.Validate(Path.Combine(output,"unlisted.zip")),"unlisted-or-missing-file");});
   Check("reject-actual-missing-dependency",()=>{Mutate(args[1],"missing.zip",(m,f)=>f.Remove(f.Keys.First(k=>k.EndsWith(".smesh"))),false);Reject(()=>PackageValidator.Validate(Path.Combine(output,"missing.zip")),"unlisted-or-missing-file");});
   Check("validated-snapshot-is-immutable",()=>{var copy=package.Scene;copy["nodes"]=new JArray();var bytes=package.Read("scene.json");byte first=bytes[0];bytes[0]^=64;Assert(((JArray)package.Scene["nodes"]).Count>0&&package.Read("scene.json")[0]==first);});
   Check("valid-instance-material-cross-reference",()=>{Mutate(args[1],"instance-valid.zip",(m,f)=>Instance(m,f,new JArray(0),true,false));PackageValidator.Validate(Path.Combine(output,"instance-valid.zip"));});
   Check("valid-ambient-gradient",()=>{Mutate(args[1],"gradient-valid.zip",(m,f)=>Gradient(m,f,""));PackageValidator.Validate(Path.Combine(output,"gradient-valid.zip"));});
   Check("valid-content-light-layers",()=>{Mutate(args[1],"layers-valid.zip",(m,f)=>Layers(m,f,new JValue(8),new JValue(272)));PackageValidator.Validate(Path.Combine(output,"layers-valid.zip"));});
   Check("valid-controlled-camera-background",()=>{Mutate(args[1],"background-valid.zip",(m,f)=>Background(m,f,new JArray(.001,.002,.004,1),false));PackageValidator.Validate(Path.Combine(output,"background-valid.zip"));});
   Check("valid-controlled-camera-clip-depth",()=>{Mutate(args[1],"clip-valid.zip",(m,f)=>Clip(m,f,63.699997,false));PackageValidator.Validate(Path.Combine(output,"clip-valid.zip"));});
   Check("valid-controlled-camera-settings",()=>{Mutate(args[1],"camera-valid.zip",(m,f)=>CameraSettings(m,f,new JValue(false),2,false));PackageValidator.Validate(Path.Combine(output,"camera-valid.zip"));});
   Check("valid-bc6h-unsigned-single-level",()=>ResourceData.ValidateTexture(Bc6()));
   Check("reject-bc6h-reserved-block-mode",()=>{var bytes=Bc6();bytes[44]=19;Reject(()=>ResourceData.ValidateTexture(bytes));});
   Check("reject-bc6h-short-block",()=>Reject(()=>ResourceData.ValidateTexture(Bc6().Take(59).ToArray())));
   Check("reject-bc6h-non-block-dimensions",()=>{var bytes=Bc6();Array.Copy(BitConverter.GetBytes(5),0,bytes,4,4);Reject(()=>ResourceData.ValidateTexture(bytes));});
   Check("reject-bc6h-srgb",()=>{var bytes=Bc6();Array.Copy(BitConverter.GetBytes(0),0,bytes,16,4);Reject(()=>ResourceData.ValidateTexture(bytes));});
   Check("reject-bc6h-cube",()=>{var bytes=Bc6();Array.Copy(BitConverter.GetBytes(6),0,bytes,28,4);Reject(()=>ResourceData.ValidateTexture(bytes));});
   Check("reject-bc6h-mips",()=>{var bytes=Bc6();Array.Copy(BitConverter.GetBytes(3),0,bytes,32,4);Reject(()=>ResourceData.ValidateTexture(bytes));});
   foreach(string effectKind in new[]{"oscillation","surface-material","planar-reflection","reflection-probe"}){string kind=effectKind;Check("valid-controlled-"+kind,()=>{string name=kind+"-valid.zip";Mutate(args[1],name,(m,f)=>SurfaceEffects(m,f,kind,""));PackageValidator.Validate(Path.Combine(output,name));});}
   var attacks=new Dictionary<string,Action<JObject,Dictionary<string,byte[]>>>{
    ["engine"]=(m,f)=>m["engine"]="2022.3.61f1",
    ["pipeline"]=(m,f)=>m["pipeline"]="14.0.11",
    ["sdk"]=(m,f)=>m["sdk"]="0.0.0",
    ["capability"]=(m,f)=>((JArray)m["capabilities"]).Add("arbitrary-script"),
    ["schema"]=(m,f)=>m["schema"]=3,
    ["color-space"]=(m,f)=>m["color_space"]="gamma",
    ["legacy-sdk"]=(m,f)=>m["sdk"]="0.1.0",
    ["control-without-capability"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["effects"]=new JArray();f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["cap-without-control"]=(m,f)=>((JArray)m["capabilities"]).Add("controlled-scene-v1"),
    ["unregistered-controlled-kind"]=(m,f)=>{((JArray)m["capabilities"]).Add("controlled-scene-v1");var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["effects"]=new JArray(new JObject{["kind"]="Awake",["node"]=0});f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["controlled-global-instance-expansion"]=(m,f)=>{((JArray)m["capabilities"]).Add("controlled-scene-v1");var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));var positions=new JArray();for(int i=0;i<60000;i++)positions.Add(new JArray(0,0,1));var effect=new JObject{["kind"]="instances",["node"]=0,["mesh"]=0,["materials"]=new JArray(0),["positions"]=positions,["scale"]=1};s["effects"]=new JArray(effect,effect.DeepClone());f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["instance-empty-materials"]=(m,f)=>Instance(m,f,new JArray(),true,false),
    ["instance-short-materials"]=(m,f)=>Instance(m,f,new JArray(0),true,true),
    ["instance-disabled-material"]=(m,f)=>Instance(m,f,new JArray(0),false,false),
    ["lightmap-mode"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["lightmapMode"]="unsupported";f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["background-color-range"]=(m,f)=>Background(m,f,new JArray(17,0,0,1),false),
    ["background-duplicate"]=(m,f)=>Background(m,f,new JArray(0,0,0,1),true),
    ["clip-depth-range"]=(m,f)=>Clip(m,f,0,false),
    ["clip-depth-duplicate"]=(m,f)=>Clip(m,f,2,true),
    ["camera-hdr-type"]=(m,f)=>CameraSettings(m,f,new JValue(1),2,false),
    ["camera-msaa-enum"]=(m,f)=>CameraSettings(m,f,new JValue(false),3,false),
    ["camera-msaa-budget"]=(m,f)=>CameraSettings(m,f,new JValue(true),16,false),
    ["camera-duplicate"]=(m,f)=>CameraSettings(m,f,new JValue(false),2,true),
    ["camera-renderer-profile"]=(m,f)=>{CameraSettings(m,f,new JValue(false),2,false);var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["effects"][0]["renderer"]="arbitrary-renderer-feature";f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["oscillation-frequency"]=(m,f)=>SurfaceEffects(m,f,"oscillation","frequency"),
    ["oscillation-root"]=(m,f)=>SurfaceEffects(m,f,"oscillation","root"),
    ["oscillation-duplicate"]=(m,f)=>SurfaceEffects(m,f,"oscillation","duplicate"),
    ["surface-material-duplicate"]=(m,f)=>SurfaceEffects(m,f,"surface-material","materials"),
    ["surface-property-type"]=(m,f)=>SurfaceEffects(m,f,"surface-material","property"),
    ["surface-producer-duplicate"]=(m,f)=>SurfaceEffects(m,f,"surface-material","duplicate"),
    ["planar-resolution"]=(m,f)=>SurfaceEffects(m,f,"planar-reflection","resolution"),
    ["planar-excluded-duplicate"]=(m,f)=>SurfaceEffects(m,f,"planar-reflection","excluded"),
    ["planar-output-property"]=(m,f)=>SurfaceEffects(m,f,"planar-reflection","property"),
    ["planar-producer-duplicate"]=(m,f)=>SurfaceEffects(m,f,"planar-reflection","duplicate"),
    ["probe-resolution-budget"]=(m,f)=>SurfaceEffects(m,f,"reflection-probe","resolution"),
    ["probe-duplicate"]=(m,f)=>SurfaceEffects(m,f,"reflection-probe","duplicate"),
    ["probe-clip-range"]=(m,f)=>SurfaceEffects(m,f,"reflection-probe","clip"),
    ["lightmap-missing-direction"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["lightmapMode"]="directional";f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["lightmap-missing-color"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["lightmaps"][0][0]=-1;f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["manifest-field"]=(m,f)=>m["script"]="Awake",
    ["missing-reference"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));((JArray)s["meshes"])[0]="missing.smesh";f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["node-event"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["nodes"][0]["events"]=new JArray("Awake");f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["ambient-gradient-color-range"]=(m,f)=>Gradient(m,f,"range"),
    ["ambient-gradient-duplicate"]=(m,f)=>Gradient(m,f,"duplicate"),
    ["ambient-gradient-with-uniform"]=(m,f)=>Gradient(m,f,"uniform"),
    ["node-layer-range"]=(m,f)=>Layers(m,f,new JValue(32),new JValue(272)),
    ["node-layer-type"]=(m,f)=>Layers(m,f,new JValue(4.5),new JValue(272)),
    ["light-mask-range"]=(m,f)=>Layers(m,f,new JValue(8),new JValue(4294967296L)),
    ["light-mask-type"]=(m,f)=>Layers(m,f,new JValue(8),new JValue(272.5)),
    ["node-parent-cycle"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));s["nodes"][0]["parent"]=0;f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["duplicate-texture-amplification"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));var textures=(JArray)s["textures"];string name=(string)textures[0];for(int i=textures.Count;i<4096;i++)textures.Add(name);f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["duplicate-mesh-amplification"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));var meshes=(JArray)s["meshes"];string name=(string)meshes[0];for(int i=meshes.Count;i<4096;i++)meshes.Add(name);f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["resource-content-alias"]=(m,f)=>{var s=JsonData.Parse(Encoding.UTF8.GetString(f["scene.json"]));var textures=(JArray)s["textures"];string name=(string)textures[0];f["textures/alias.stex"]=(byte[])f[name].Clone();textures.Add("textures/alias.stex");f["scene.json"]=Encoding.UTF8.GetBytes(s.ToString());},
    ["native-behavior-disguised"]=(m,f)=>f["hidden.shaderbundle"]=Encoding.UTF8.GetBytes("UnityFS\0evil-callback"),
    ["mesh-invalid-index"]=(m,f)=>{string key=f.Keys.First(k=>k.EndsWith(".smesh"));var b=f[key];Array.Copy(BitConverter.GetBytes(int.MaxValue),0,b,b.Length-4,4);},
    ["texture-stream-config"]=(m,f)=>{string key=f.Keys.First(k=>k.EndsWith(".stex"));f[key]=f[key].Concat(Encoding.UTF8.GetBytes("file:///sensitive")).ToArray();},
    ["forbidden-file"]=(m,f)=>f["hidden.dll"]=new byte[]{1,2,3},
    ["unverified-share"]=(m,f)=>m["license"]["mode"]="share"
   };
   foreach(var attack in attacks){Check("reject-rehashed-"+attack.Key,()=>{
    string name="attack-"+attack.Key+".zip";Mutate(args[1],name,attack.Value);
    string reason=attack.Key.StartsWith("duplicate-")?"duplicate-resource-reference":attack.Key=="resource-content-alias"?"texture-resource-alias":attack.Key=="mesh-invalid-index"?"mesh-index":attack.Key=="texture-stream-config"?"texture-size":attack.Key=="controlled-global-instance-expansion"?"instance-total-limit":attack.Key=="unregistered-controlled-kind"?"unsupported-controlled-kind":attack.Key=="control-without-capability"||attack.Key=="cap-without-control"?"controlled-capability-mismatch":null;
    if(attack.Key.StartsWith("instance-"))reason=attack.Key=="instance-disabled-material"?"instance-material-disabled":"instance-material-count";
    Reject(()=>PackageValidator.Validate(Path.Combine(output,name)),reason);
   });}
   Check("reject-digest-mismatch",()=>{Mutate(args[1],"digest.zip",(m,f)=>f["scene.json"][0]=32,false);Reject(()=>PackageValidator.Validate(Path.Combine(output,"digest.zip")));});
   Check("reject-identity-mismatch",()=>{Mutate(args[1],"identity.zip",(m,f)=>m["name"]="replaced",false);Reject(()=>PackageValidator.Validate(Path.Combine(output,"identity.zip")));});
   byte[] data=File.ReadAllBytes(args[1]);data[data.Length/2]^=64;File.WriteAllBytes(Path.Combine(output,"corrupt.zip"),data);
   Check("reject-corrupt-package",()=>Reject(()=>PackageValidator.Validate(Path.Combine(output,"corrupt.zip"))));
  }
  Console.WriteLine($"{passed} passed; {failed} failed");Environment.Exit(failed==0?0:1);
 }
 static void Gradient(JObject manifest,Dictionary<string,byte[]> files,string mutation){
  manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));var effect=new JObject{["kind"]="ambient-gradient",["node"]=0,["sky"]=new JArray(.32,.38,.44,1),["equator"]=new JArray(.2,.23,.22,1),["ground"]=new JArray(.1,.14,.13,1)};
  if(mutation=="range")effect["ground"][0]=17;var effects=new JArray(effect);if(mutation=="duplicate")effects.Add(effect.DeepClone());if(mutation=="uniform")effects.Add(new JObject{["kind"]="ambient",["node"]=0,["irradiance"]=new JArray(.1,.1,.1,1)});scene["effects"]=effects;files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
 }
 static void Layers(JObject manifest,Dictionary<string,byte[]> files,JToken layer,JToken mask){
  manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));scene["nodes"][0]["layer"]=layer;
  scene["effects"]=new JArray(new JObject{["kind"]="light",["node"]=0,["type"]="directional",["color"]=new JArray(1,1,1,1),["intensity"]=1,["range"]=1,["spot"]=30,["innerSpot"]=20,["shadows"]=0,["bias"]=0,["normalBias"]=0,["near"]=.01,["mask"]=mask});files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
 }
 static void Background(JObject manifest,Dictionary<string,byte[]> files,JArray color,bool duplicate){
  manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));var effect=new JObject{["kind"]="background",["node"]=0,["color"]=color};scene["effects"]=duplicate?new JArray(effect,effect.DeepClone()):new JArray(effect);files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
 }
 static void Clip(JObject manifest,Dictionary<string,byte[]> files,double depth,bool duplicate){
  manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));var effect=new JObject{["kind"]="clip-depth",["node"]=0,["depth"]=depth};scene["effects"]=duplicate?new JArray(effect,effect.DeepClone()):new JArray(effect);files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
 }
  static void CameraSettings(JObject manifest,Dictionary<string,byte[]> files,JToken hdr,int msaa,bool duplicate){
   manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));var effect=new JObject{["kind"]="camera",["node"]=0,["hdr"]=hdr,["msaa"]=msaa,["opaque"]=false,["depth"]=false,["post"]=false,["renderer"]="forward"};scene["effects"]=duplicate?new JArray(effect,effect.DeepClone()):new JArray(effect);files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
  }
  static byte[] Bc6(){using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(0x32545753u);foreach(int value in new[]{4,4,24,1,0,1,1,1,1})writer.Write(value);writer.Write(0f);writer.Write(new byte[16]);return stream.ToArray();}}
  static void SurfaceEffects(JObject manifest,Dictionary<string,byte[]> files,string kind,string mutation){
   manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));var nodes=(JArray)scene["nodes"];int target=Enumerable.Range(0,nodes.Count).First(i=>(int)nodes[i]["parent"]>=0&&(int)nodes[i]["mesh"]>=0);var props=(JObject)scene["materials"][0]["properties"];JObject effect;
   if(kind=="oscillation"){
    var harmonic=new JObject{["amplitude"]=new JArray(.04,.045,.03),["frequency"]=new JArray(.31,mutation=="frequency"?33:1.1,.23),["phase"]=new JArray(0,0,0)};effect=new JObject{["kind"]=kind,["node"]=mutation=="root"?0:target,["position"]=new JArray(harmonic),["rotation"]=new JArray()};
   }else if(kind=="surface-material"){
    props["_WaterLevel"]=new JObject{["type"]="float",["value"]=0};props["_WorldScale"]=new JObject{["type"]="float",["value"]=.05};props["_Animation"]=new JObject{["type"]="float",["value"]=1};if(mutation=="property")props["_WorldScale"]=new JObject{["type"]="color",["value"]=new JArray(1,1,1,1)};
    effect=new JObject{["kind"]=kind,["node"]=target,["materials"]=mutation=="materials"?new JArray(0,0):new JArray(0),["depthPerScale"]=2.5,["tiling"]=new JArray(.2,.2,.3,.3)};
   }else if(kind=="reflection-probe"){
    effect=new JObject{["kind"]=kind,["node"]=target,["resolution"]=mutation=="resolution"?256:128,["size"]=new JArray(2,2,2),["center"]=new JArray(0,0,0),["near"]=.01,["far"]=mutation=="clip"?.001:2,["shadowDistance"]=100,["background"]=new JArray(.4,.5,.6,1),["intensity"]=1,["blend"]=1,["box"]=false,["importance"]=1};
   }else{
    if(mutation!="property")props["_ReflectionTex"]=new JObject{["type"]="texture",["value"]=-1};effect=new JObject{["kind"]=kind,["node"]=target,["material"]=0,["resolution"]=mutation=="resolution"?1:.5,["excluded"]=mutation=="excluded"?new JArray(target,target):new JArray(target)};
   }
   scene["effects"]=mutation=="duplicate"?new JArray(effect,effect.DeepClone()):new JArray(effect);files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
  }
 static void Instance(JObject manifest,Dictionary<string,byte[]> files,JArray materials,bool instancing,bool secondSubmesh){
  manifest["capabilities"]=new JArray("static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1");var scene=JsonData.Parse(Encoding.UTF8.GetString(files["scene.json"]));
  scene["materials"][0]["instancing"]=instancing;
  if(secondSubmesh){string path=(string)scene["meshes"][0];var bytes=files[path];Assert(BitConverter.ToInt32(bytes,8)==1);int start=12+BitConverter.ToInt32(bytes,4)*72;var extended=bytes.Concat(bytes.Skip(start)).ToArray();Array.Copy(BitConverter.GetBytes(2),0,extended,8,4);files[path]=extended;}
  scene["effects"]=new JArray(new JObject{["kind"]="instances",["node"]=0,["mesh"]=0,["materials"]=materials,["positions"]=new JArray{new JArray(100000,0,1)},["scale"]=1});files["scene.json"]=Encoding.UTF8.GetBytes(scene.ToString());
 }
}

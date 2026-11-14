using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SpatialWallpaper.Blueprint;

static class MeshChecks {
 static int passed,failed;static object catalog;
 static Type Type(string name)=>Assembly.GetExecutingAssembly().GetType("SpatialWallpaper.Blueprint."+name)??throw new Exception(name+" is not implemented");
 static object Invoke(string name,string method,params object[] args){try{return Type(name).GetMethod(method).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static dynamic Compile(BlueprintDocument document)=>Invoke("BlockMeshCompiler","Compile",document,catalog,CancellationToken.None);
 static BlueprintDocument Doc(params string[] states){var d=Fixtures.Sponge(2);((fNbt.NbtShort)d["Width"]).Value=(short)states.Length;var unique=states.Distinct().ToArray();var palette=new fNbt.NbtCompound("Palette");for(int i=0;i<unique.Length;i++)palette.Add(new fNbt.NbtInt(unique[i],i));d["Palette"]=palette;d["BlockData"]=new fNbt.NbtByteArray("BlockData",states.Select(s=>(byte)Array.IndexOf(unique,s)).ToArray());return BlueprintReader.Read(Fixtures.Encode(d),"fixture.schem",CancellationToken.None);}
 static IEnumerable<dynamic> Parts(dynamic c){foreach(dynamic part in (IEnumerable)c.Parts)yield return part;}
 static int Vertices(dynamic c){int n=0;foreach(dynamic p in Parts((object)c))n+=((float[])p.Positions).Length/3;return n;}
 static void Eq(object a,object b){if(!Equals(a,b))throw new Exception("expected "+b+", got "+a);}
 static void Check(string name,Action run){try{run();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
 static void Reject(Action run,string code){try{run();}catch(Exception e){if(e.GetBaseException().Message.Contains(code))return;throw;}throw new Exception("expected "+code);}
 public static int Main(string[] args){
  Check("catalog-real-pixel-pack",()=>{catalog=Invoke("ModelCatalog","Load",File.ReadAllText(args[0]));dynamic c=catalog;Eq((bool)c.HasTexture("cherry_planks"),true);Eq((bool)c.HasTexture("oak_planks"),true);});
  Check("single-cube-six-faces",()=>{dynamic c=Compile(Doc("minecraft:stone"));Eq(Vertices(c),24);Eq((int)c.TriangleCount,12);});
  Check("opaque-neighbour-removes-both-interior-faces",()=>{dynamic c=Compile(Doc("minecraft:stone","minecraft:stone"));Eq(Vertices(c),40);Eq((int)c.TriangleCount,20);});
  Check("hidden-faces-across-space-batches",()=>{var states=Enumerable.Repeat("minecraft:air",18).ToArray();states[15]="minecraft:stone";states[16]="minecraft:stone";dynamic c=Compile(Doc(states));Eq(Vertices(c),40);});
  Check("upper-slab-keeps-half-height",()=>{dynamic c=Compile(Doc("minecraft:oak_slab[type=top]"));var ys=Parts((object)c).SelectMany(p=>(float[])p.Positions).Where((v,i)=>i%3==1).ToArray();Eq(ys.Min(),.5f);Eq(ys.Max(),1f);});
  Check("stairs-are-not-cubes",()=>{dynamic c=Compile(Doc("minecraft:cherry_stairs[facing=north,half=bottom,shape=straight]"));Eq((int)c.MissingStates.Count,0);if(Vertices(c)<=24)throw new Exception("stair profile missing");});
  Check("stair-top-and-bottom-have-different-geometry",()=>{dynamic a=Compile(Doc("minecraft:oak_stairs[facing=east,half=top,shape=outer_left]")),b=Compile(Doc("minecraft:oak_stairs[facing=east,half=bottom,shape=outer_left]"));var pa=Parts((object)a).SelectMany(p=>(float[])p.Positions).ToArray();var pb=Parts((object)b).SelectMany(p=>(float[])p.Positions).ToArray();Eq(pa.SequenceEqual(pb),false);});
  Check("plants-use-cutout-cross-geometry",()=>{dynamic c=Compile(Doc("minecraft:allium"));Eq((int)c.MissingStates.Count,0);if(!Parts((object)c).Any(p=>p.Kind.ToString()=="Cutout"))throw new Exception("plant missing cutout group");if(Vertices(c)==24)throw new Exception("plant became a cube");});
  Check("connected-pane-preserves-thin-shape",()=>{dynamic c=Compile(Doc("minecraft:glass_pane[north=true,south=true,east=false,west=false]"));var xs=Parts((object)c).SelectMany(p=>(float[])p.Positions).Where((v,i)=>i%3==0).ToArray();if(xs.Max()-xs.Min()>=.25f)throw new Exception("pane stretched to full block");});
  Check("door-open-state-changes-shape",()=>{dynamic a=Compile(Doc("minecraft:cherry_door[facing=north,half=lower,hinge=left,open=false]")),b=Compile(Doc("minecraft:cherry_door[facing=north,half=lower,hinge=left,open=true]"));Eq(Parts((object)a).SelectMany(p=>(float[])p.Positions).SequenceEqual(Parts((object)b).SelectMany(p=>(float[])p.Positions)),false);});
  Check("unsupported-mod-state-remains-visible-report",()=>{dynamic c=Compile(Doc("some_mod:stone"));Eq((int)c.MissingStates.Count,1);Eq((bool)c.CanApply,false);});
  Check("texture-does-not-prove-cube-model",()=>{foreach(string name in new[]{"ladder","rail","vine","farmland","flower_pot"}){dynamic c=Compile(Doc("minecraft:"+name));Eq((bool)c.CanApply,false);}});
  Check("water-material-separated-from-solid",()=>{dynamic c=Compile(Doc("minecraft:water[level=0]"));Eq((int)c.MissingStates.Count,0);Eq(Parts((object)c).Any(p=>p.Kind.ToString()=="Water"),true);});
  Check("campfire-has-supported-static-log-and-flame-model",()=>{dynamic c=Compile(Doc("minecraft:campfire[lit=true,facing=north,waterlogged=false]"));Eq((bool)c.CanApply,true);Eq(Parts((object)c).Any(p=>p.Kind.ToString()=="Glow"),true);});
  Check("lantern-body-samples-lit-pixels",()=>{var model=((ModelCatalog)catalog).Resolve(new BlockState("minecraft:lantern"));foreach(var q in model.Quads.Where(q=>q.Kind==BlockRenderKind.Glow))if(q.Uv.Any(uv=>uv.X>6f/16||uv.Y<10f/16))throw new Exception("lantern samples transparent unused texture area");});
  Check("waterlogged-slab-adds-free-water-surface",()=>{dynamic c=Compile(Doc("minecraft:oak_slab[type=bottom,waterlogged=true]"));Eq(Parts((object)c).Any(p=>p.Kind.ToString()=="Water"),true);foreach(dynamic p in Parts((object)c))if(p.Kind.ToString()=="Water"){var ys=((float[])p.Positions).Where((v,i)=>i%3==1);if(ys.Any(v=>v<.499f))throw new Exception("water overlaps solid lower slab");}});
  Check("face-winding-matches-supplied-normal",()=>{dynamic c=Compile(Doc("minecraft:stone","minecraft:oak_stairs[facing=west,half=top,shape=inner_right]"));foreach(dynamic p in Parts((object)c)){float[] v=p.Positions,n=p.Normals;int[] indices=p.Indices;for(int i=0;i<indices.Length;i+=3){int a=indices[i]*3,b=indices[i+1]*3,d=indices[i+2]*3;float ax=v[b]-v[a],ay=v[b+1]-v[a+1],az=v[b+2]-v[a+2],bx=v[d]-v[a],by=v[d+1]-v[a+1],bz=v[d+2]-v[a+2];float dot=(ay*bz-az*by)*n[a]+(az*bx-ax*bz)*n[a+1]+(ax*by-ay*bx)*n[a+2];if(dot<=0)throw new Exception("reversed or degenerate triangle");}}});
  Check("uv-stays-inside-atlas",()=>{dynamic c=Compile(Doc("minecraft:cherry_planks","minecraft:oak_log[axis=x]"));foreach(dynamic p in Parts((object)c))foreach(float v in (float[])p.Uv)if(v<0||v>1||float.IsNaN(v))throw new Exception("invalid UV");});
  Check("cache-roundtrip-and-identity",()=>{dynamic c=Compile(Doc("minecraft:stone","minecraft:cherry_stairs[facing=east]"));byte[] bytes=(byte[])Invoke("BlueprintCache","Write",c,CancellationToken.None);dynamic loaded=Invoke("BlueprintCache","Read",bytes,(string)c.CacheKey);Eq(Vertices(loaded),Vertices(c));Eq((int)loaded.TriangleCount,(int)c.TriangleCount);Reject(()=>Invoke("BlueprintCache","Read",bytes,"wrong-key"),"cache-identity");});
  Check("cache-rejects-truncation",()=>{dynamic c=Compile(Doc("minecraft:stone"));byte[] bytes=(byte[])Invoke("BlueprintCache","Write",c,CancellationToken.None);Reject(()=>Invoke("BlueprintCache","Read",bytes.Take(bytes.Length/2).ToArray(),(string)c.CacheKey),"cache");});
  Check("file-cache-repairs-corrupt-entry",()=>{var c=(CompiledBlueprint)Compile(Doc("minecraft:stone"));string root=Path.Combine(Path.GetTempPath(),"blueprint-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{string path=Path.Combine(root,c.CacheKey+".swbc");File.WriteAllBytes(path,new byte[]{1,2,3});long bytes;Eq(BlueprintCacheFiles.Read(root,c.CacheKey,out bytes),null);Eq(File.Exists(path),false);long written=BlueprintCacheFiles.Write(root,c,CancellationToken.None);var saved=BlueprintCacheFiles.Read(root,c.CacheKey,out bytes);Eq(saved.TriangleCount,c.TriangleCount);Eq(bytes,written);Eq(Directory.GetFiles(root,"*.tmp").Length,0);}finally{Directory.Delete(root,true);}});
  if(args.Length>1&&File.Exists(args[1]))Check("user-cottage-all-83-states-supported",()=>{var doc=BlueprintReader.Read(File.ReadAllBytes(args[1]),"user.zip",CancellationToken.None);dynamic c=Compile(doc);if(c.MissingStates.Count!=0)throw new Exception(string.Join(",",((IEnumerable)c.MissingStates).Cast<object>()));if(Vertices(c)<1000)throw new Exception("missing building geometry");byte[] bytes=(byte[])Invoke("BlueprintCache","Write",c,CancellationToken.None);Console.WriteLine("User cottage: "+Vertices(c)+" vertices, "+c.TriangleCount+" triangles, "+bytes.Length+" compressed cache bytes");});
  Console.WriteLine("Blueprint mesh: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}

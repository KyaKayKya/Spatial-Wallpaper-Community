using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialWallpaper.Blueprint {
 public sealed class BlockModelResolver {
  readonly ModelCatalog catalog;public BlockModelResolver(ModelCatalog catalog){this.catalog=catalog;}
  static readonly string[] Woods={"oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry","bamboo","crimson","warped"};
  static readonly HashSet<string> Cubes=new HashSet<string>(("stone granite polished_granite diorite polished_diorite andesite polished_andesite cobblestone mossy_cobblestone deepslate cobbled_deepslate polished_deepslate calcite tuff polished_tuff dirt coarse_dirt rooted_dirt mud packed_mud clay sand red_sand gravel bedrock netherrack soul_sand soul_soil end_stone obsidian crying_obsidian glowstone shroomlight magma_block sponge wet_sponge terracotta bricks prismarine dark_prismarine sea_lantern redstone_lamp bookshelf chiseled_bookshelf crafting_table furnace blast_furnace smoker dispenser dropper observer note_block jukebox barrel target tnt melon pumpkin carved_pumpkin jack_o_lantern cactus dried_kelp_block hay_block bone_block purpur_pillar quartz_pillar smooth_stone smooth_sandstone smooth_red_sandstone sandstone cut_sandstone chiseled_sandstone red_sandstone cut_red_sandstone chiseled_red_sandstone blackstone polished_blackstone gilded_blackstone chiseled_polished_blackstone chiseled_deepslate reinforced_deepslate chiseled_tuff chiseled_tuff_bricks amethyst_block budding_amethyst sculk lodestone respawn_anchor beehive bee_nest honeycomb_block honey_block slime_block ancient_debris mushroom_stem brown_mushroom_block red_mushroom_block").Split(' '),StringComparer.Ordinal);
  static bool Cube(string name)=>Cubes.Contains(name)||new[]{"_planks","_wool","_concrete","_concrete_powder","_terracotta","_bricks","_tiles","_ore","_block"}.Any(s=>name.EndsWith(s,StringComparison.Ordinal));
  static readonly HashSet<string> Plants=new HashSet<string>(new[]{"grass","short_grass","tall_grass","fern","large_fern","dead_bush","allium","blue_orchid","dandelion","poppy","azure_bluet","oxeye_daisy","cornflower","lily_of_the_valley","wither_rose","red_tulip","white_tulip","pink_tulip","orange_tulip","brown_mushroom","red_mushroom","sugar_cane","crimson_fungus","warped_fungus","crimson_roots","warped_roots","nether_sprouts","sweet_berry_bush","wheat","carrots","potatoes","beetroots","torchflower","pink_petals","seagrass","tall_seagrass","bamboo","sunflower","lilac","rose_bush","peony"},StringComparer.Ordinal);
  static ShapeBox Box(float x,float y,float z,float xx,float yy,float zz,string texture,BlockRenderKind kind=BlockRenderKind.Opaque,byte tint=0)=>new ShapeBox(new Float3(x,y,z),new Float3(xx,yy,zz),texture,kind,tint);
  static float Facing(string facing)=>facing=="east"?-90:facing=="south"?180:facing=="west"?90:0;
  static float StairFacing(string facing)=>facing=="south"?-90:facing=="west"?180:facing=="north"?90:0;
  string Pick(params string[] choices)=>choices.FirstOrDefault(c=>catalog.HasTexture(c))??choices[0];
  string Material(string name){
   foreach(string suffix in new[]{"_stairs","_slab","_wall","_fence_gate","_fence","_button","_pressure_plate"})if(name.EndsWith(suffix,StringComparison.Ordinal)){name=name.Substring(0,name.Length-suffix.Length);break;}
   if(Woods.Contains(name))return name+"_planks";
   var aliases=new Dictionary<string,string>{{"stone_brick","stone_bricks"},{"mossy_stone_brick","mossy_stone_bricks"},{"cracked_stone_brick","cracked_stone_bricks"},{"brick","bricks"},{"mud_brick","mud_bricks"},{"nether_brick","nether_bricks"},{"red_nether_brick","red_nether_bricks"},{"end_stone_brick","end_stone_bricks"},{"prismarine_brick","prismarine_bricks"},{"quartz","quartz_block_side"},{"smooth_quartz","quartz_block_side"},{"purpur","purpur_block"},{"stone","stone"},{"deepslate_brick","deepslate_bricks"},{"deepslate_tile","deepslate_tiles"}};
   return aliases.TryGetValue(name,out string value)?value:name;
  }
  public BlockModel Resolve(BlockState state){
   var model=new BlockModel();if(state.IsAir)return model;
   string n=state.Name.StartsWith("minecraft:",StringComparison.Ordinal)?state.Name.Substring(10):null;
   if(n==null)return Missing(state.Key);
   var boxes=new List<ShapeBox>();string texture=Material(n);string facing=state.Property("facing","north");
   if(n.EndsWith("_stairs",StringComparison.Ordinal)){
    string half=state.Property("half","bottom"),shape=state.Property("shape","straight");bool top=half=="top";
    if(!new[]{"north","south","east","west"}.Contains(facing)||!new[]{"bottom","top"}.Contains(half)||!new[]{"straight","inner_left","inner_right","outer_left","outer_right"}.Contains(shape))return Missing(state.Key);
    boxes.Add(Box(0,top?.5f:0,0,1,top?1:.5f,1,texture));float y=top?0:.5f,yy=top?.5f:1;
    if(shape.StartsWith("outer",StringComparison.Ordinal))boxes.Add(Box(.5f,y,shape=="outer_left"?0:.5f,1,yy,shape=="outer_left"?.5f:1,texture));
    else{boxes.Add(Box(.5f,y,0,1,yy,1,texture));if(shape.StartsWith("inner",StringComparison.Ordinal))boxes.Add(Box(0,y,shape=="inner_left"?0:.5f,.5f,yy,shape=="inner_left"?.5f:1,texture));}
    Finish(model,boxes,state);ShapeGeometry.RotateY(model,StairFacing(facing),new Float3(.5f,.5f,.5f));
   }else if(n.EndsWith("_slab",StringComparison.Ordinal)){
    string type=state.Property("type","bottom");if(!new[]{"top","bottom","double"}.Contains(type))return Missing(state.Key);boxes.Add(Box(0,type=="top"?.5f:0,0,1,type=="bottom"?.5f:1,1,texture));model.FullCube=type=="double";
   }else if(n.EndsWith("_trapdoor",StringComparison.Ordinal)){
    texture=n;bool open=state.Property("open","false")=="true";float y=state.Property("half","bottom")=="top"?.8125f:0;
    boxes.Add(open?Box(0,0,.8125f,1,1,1,texture,BlockRenderKind.Cutout):Box(0,y,0,1,y+.1875f,1,texture,BlockRenderKind.Cutout));Finish(model,boxes,state);ShapeGeometry.RotateY(model,Facing(facing),new Float3(.5f,.5f,.5f));
   }else if(n.EndsWith("_door",StringComparison.Ordinal)){
    texture=n+(state.Property("half","lower")=="upper"?"_top":"_bottom");boxes.Add(Box(0,0,0,1,1,.1875f,texture,BlockRenderKind.Cutout));Finish(model,boxes,state);
    if(state.Property("open","false")=="true"){bool right=state.Property("hinge","left")=="right";ShapeGeometry.RotateY(model,right?-90:90,new Float3(right?1:0,0,0));}
    ShapeGeometry.RotateY(model,Facing(facing),new Float3(.5f,.5f,.5f));
   }else if(n.EndsWith("_fence_gate",StringComparison.Ordinal)){
    boxes.Add(Box(0,.3125f,.375f,.125f,1,.625f,texture));boxes.Add(Box(.875f,.3125f,.375f,1,1,.625f,texture));
    if(state.Property("open","false")!="true"){boxes.Add(Box(.125f,.375f,.4375f,.875f,.5625f,.5625f,texture));boxes.Add(Box(.125f,.75f,.4375f,.875f,.9375f,.5625f,texture));}
    Finish(model,boxes,state);ShapeGeometry.RotateY(model,Facing(facing),new Float3(.5f,.5f,.5f));
   }else if(n.EndsWith("_fence",StringComparison.Ordinal)){
    boxes.Add(Box(.375f,0,.375f,.625f,1,.625f,texture));foreach(string dir in new[]{"north","south","east","west"})if(state.Property(dir,"false")=="true")foreach(float y in new[]{.375f,.75f}){
     boxes.Add(dir=="north"?Box(.4375f,y,0,.5625f,y+.1875f,.5f,texture):dir=="south"?Box(.4375f,y,.5f,.5625f,y+.1875f,1,texture):dir=="east"?Box(.5f,y,.4375f,1,y+.1875f,.5625f,texture):Box(0,y,.4375f,.5f,y+.1875f,.5625f,texture));
    }
   }else if(n.EndsWith("_wall",StringComparison.Ordinal)){
    if(state.Property("up","true")=="true")boxes.Add(Box(.25f,0,.25f,.75f,1,.75f,texture));foreach(string dir in new[]{"north","south","east","west"}){string value=state.Property(dir,"none");if(value=="none"||value=="false")continue;float h=value=="tall"?1:.875f;boxes.Add(dir=="north"?Box(.3125f,0,0,.6875f,h,.5f,texture):dir=="south"?Box(.3125f,0,.5f,.6875f,h,1,texture):dir=="east"?Box(.5f,0,.3125f,1,h,.6875f,texture):Box(0,0,.3125f,.5f,h,.6875f,texture));}
   }else if(n=="glass_pane"||n.EndsWith("_stained_glass_pane",StringComparison.Ordinal)||n=="iron_bars"){
    texture=n=="glass_pane"?"glass":n=="iron_bars"?n:n.Replace("_pane","");var kind=n=="iron_bars"?BlockRenderKind.Cutout:BlockRenderKind.Glass;
    boxes.Add(Box(.4375f,0,.4375f,.5625f,1,.5625f,texture,kind));foreach(string dir in new[]{"north","south","east","west"})if(state.Property(dir,"false")=="true")boxes.Add(dir=="north"?Box(.4375f,0,0,.5625f,1,.5f,texture,kind):dir=="south"?Box(.4375f,0,.5f,.5625f,1,1,texture,kind):dir=="east"?Box(.5f,0,.4375f,1,1,.5625f,texture,kind):Box(0,0,.4375f,.5f,1,.5625f,texture,kind));
   }else if(n=="chain"){
    ShapeGeometry.Cross(model,"chain");string axis=state.Property("axis","y");if(axis!="y")foreach(var q in model.Quads){for(int i=0;i<4;i++){var p=q.Points[i];q.Points[i]=axis=="x"?new Float3(p.Y,p.X,p.Z):new Float3(p.X,p.Z,p.Y);}q.Normal=Float3.Cross(q.Points[1]-q.Points[0],q.Points[2]-q.Points[0]).Normalized();}
   }else if(n.EndsWith("_button",StringComparison.Ordinal)){
    string face=state.Property("face","wall");float depth=state.Property("powered","false")=="true"?.0625f:.125f;
    boxes.Add(face=="floor"?Box(.3125f,0,.375f,.6875f,depth,.625f,texture):face=="ceiling"?Box(.3125f,1-depth,.375f,.6875f,1,.625f,texture):Box(.3125f,.375f,1-depth,.6875f,.625f,1,texture));Finish(model,boxes,state);ShapeGeometry.RotateY(model,Facing(facing),new Float3(.5f,.5f,.5f));
   }else if(n.EndsWith("_pressure_plate",StringComparison.Ordinal)||n.EndsWith("_carpet",StringComparison.Ordinal)||n=="snow"){
    if(n.EndsWith("_carpet",StringComparison.Ordinal))texture=n.Replace("_carpet","_wool");if(n=="snow")texture="snow";float h=n=="snow"?Math.Max(1,Math.Min(8,Int(state.Property("layers","1"),1)))/8f:.0625f;boxes.Add(Box(0,0,0,1,h,1,texture));
   }else if(n=="lantern"||n=="soul_lantern"){
    float y=state.Property("hanging","false")=="true"?.0625f:0;boxes.Add(Box(.3125f,y,.3125f,.6875f,y+.4375f,.6875f,n,BlockRenderKind.Glow));boxes.Add(Box(.25f,y+.4375f,.25f,.75f,y+.5625f,.75f,n));boxes.Add(Box(.4375f,y+.5625f,.4375f,.5625f,y+.8125f,.5625f,n));
    Finish(model,boxes,state);foreach(var q in model.Quads)if(q.Kind==BlockRenderKind.Glow)q.Uv=new[]{new Float2(0,10f/16),new Float2(0,1),new Float2(6f/16,1),new Float2(6f/16,10f/16)};
   }else if(n=="campfire"||n=="soul_campfire"){
    bool lit=state.Property("lit","true")=="true";string log=Pick(lit?"campfire_log_lit":"campfire_log","campfire_log");foreach(float z in new[]{.125f,.625f})boxes.Add(Box(0,0,z,1,.25f,z+.25f,log));foreach(float x in new[]{.125f,.625f})boxes.Add(Box(x,.125f,0,x+.25f,.375f,1,log));Finish(model,boxes,state);if(lit){int first=model.Quads.Count;ShapeGeometry.Cross(model,Pick(n+"_fire","campfire_fire"),BlockRenderKind.Glow,0,.65f);for(int i=first;i<model.Quads.Count;i++){var q=model.Quads[i];q.Wind=false;for(int v=0;v<4;v++)q.Points[v]=q.Points[v]+new Float3(0,.125f,0);}}ShapeGeometry.RotateY(model,Facing(facing),new Float3(.5f,0,.5f));
   }else if(n=="torch"||n=="wall_torch"||n=="soul_torch"||n=="soul_wall_torch"||n=="redstone_torch"||n=="redstone_wall_torch"){
    texture=n.Replace("wall_","");boxes.Add(Box(.4375f,0,.4375f,.5625f,.625f,.5625f,texture,BlockRenderKind.Glow));
   }else if(Plants.Contains(n)){
    texture=n=="short_grass"?Pick("short_grass","grass"):n=="tall_grass"?Pick("tall_grass_"+(state.Property("half","lower")=="upper"?"top":"bottom"),"grass"):n;
    if(new[]{"wheat","carrots","potatoes","beetroots","sweet_berry_bush"}.Contains(n))texture=Pick(n+"_stage"+state.Property("age","0"),n+"_stage_"+state.Property("age","0"),n);
    ShapeGeometry.Cross(model,texture,BlockRenderKind.Cutout,(byte)(n.Contains("grass")||n.Contains("fern")?1:0));
   }else if(n=="lily_pad"){
    ShapeGeometry.Plane(model,new[]{new Float3(0,.015f,1),new Float3(1,.015f,1),new Float3(1,.015f,0),new Float3(0,.015f,0)},"lily_pad",BlockRenderKind.Cutout,2,true);
   }else if(n=="water"||n=="lava"){
    boxes.Add(Box(0,0,0,1,1,1,Pick(n+"_still",n),n=="water"?BlockRenderKind.Water:BlockRenderKind.Glow));
   }else if(n=="grass_block"||n=="mycelium"||n=="podzol"){
    var box=Box(0,0,0,1,1,1,n=="grass_block"?"grass_block_side":n+"_side");box.Textures[2]=n=="grass_block"?"grass_block_top":n+"_top";box.Textures[3]="dirt";if(n=="grass_block")box.Tints[2]=1;boxes.Add(box);model.FullCube=true;
   }else if(n.EndsWith("_log",StringComparison.Ordinal)||n.EndsWith("_wood",StringComparison.Ordinal)||n.EndsWith("_stem",StringComparison.Ordinal)||n.EndsWith("_hyphae",StringComparison.Ordinal)){
    string side=n.EndsWith("_wood",StringComparison.Ordinal)?n.Replace("_wood","_log"):n.EndsWith("_hyphae",StringComparison.Ordinal)?n.Replace("_hyphae","_stem"):n;
    var box=Box(0,0,0,1,1,1,side);string axis=state.Property("axis","y");if(!n.EndsWith("_wood",StringComparison.Ordinal)&&!n.EndsWith("_hyphae",StringComparison.Ordinal)){int f=axis=="x"?0:axis=="z"?4:2;box.Textures[f]=box.Textures[f+1]=Pick(side+"_top",side);}boxes.Add(box);model.FullCube=true;
   }else if(n.EndsWith("_leaves",StringComparison.Ordinal))boxes.Add(Box(0,0,0,1,1,1,n,BlockRenderKind.Cutout,2));
   else if(n=="glass"||n.EndsWith("_stained_glass",StringComparison.Ordinal)||n=="ice")boxes.Add(Box(0,0,0,1,1,1,n,BlockRenderKind.Glass));
   else if(Cube(n)&&catalog.HasTexture(n)){boxes.Add(Box(0,0,0,1,1,1,n));model.FullCube=true;}
   else if(Cube(n)&&catalog.HasTexture(n+"_side")){var box=Box(0,0,0,1,1,1,n+"_side");box.Textures[2]=Pick(n+"_top",n+"_side");box.Textures[3]=Pick(n+"_bottom",n+"_top",n+"_side");boxes.Add(box);model.FullCube=true;}
   else return Missing(state.Key);
   if(model.Quads.Count==0)Finish(model,boxes,state);
   if(n=="grass_block"&&catalog.HasTexture("grass_block_side_overlay"))foreach(int face in new[]{0,1,4,5}){var q=ShapeGeometry.Face(face,face%2==0?1.0002f:-.0002f,new FaceRect(0,0,1,1),"grass_block_side_overlay",BlockRenderKind.Cutout,1);q.CullFace=face;model.Quads.Add(q);}
   foreach(var q in model.Quads)if(!catalog.HasTexture(q.Texture)){model.Supported=false;model.Problem="missing texture "+q.Texture;}
   if(state.Property("waterlogged","false")=="true"&&!model.Waterlogged)ShapeGeometry.Fluid(model,boxes,Pick("water_still","water"));return model;
  }
  void Finish(BlockModel model,List<ShapeBox> boxes,BlockState state){ShapeGeometry.Boxes(model,boxes);if(state.Property("waterlogged","false")=="true")ShapeGeometry.Fluid(model,boxes,Pick("water_still","water"));}
  static int Int(string value,int fallback)=>int.TryParse(value,out int v)?v:fallback;
  BlockModel Missing(string key){var m=new BlockModel{Supported=false,Problem="unsupported "+key};ShapeGeometry.Boxes(m,new List<ShapeBox>{Box(0,0,0,1,1,1,"magenta_concrete")});return m;}
 }
}

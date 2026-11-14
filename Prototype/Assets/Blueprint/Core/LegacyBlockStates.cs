using System.Collections.Generic;
namespace SpatialWallpaper.Blueprint {
 public static class LegacyBlockStates {
  static readonly string[] Woods={"oak","spruce","birch","jungle","acacia","dark_oak"};
  static readonly string[] Colors={"white","orange","magenta","light_blue","yellow","lime","pink","gray","light_gray","cyan","purple","blue","brown","green","red","black"};
  static readonly Dictionary<int,string> Simple=new Dictionary<int,string>{{0,"air"},{2,"grass_block"},{4,"cobblestone"},{7,"bedrock"},{8,"water"},{9,"water"},{10,"lava"},{11,"lava"},{12,"sand"},{13,"gravel"},{14,"gold_ore"},{15,"iron_ore"},{16,"coal_ore"},{20,"glass"},{21,"lapis_ore"},{22,"lapis_block"},{24,"sandstone"},{30,"cobweb"},{37,"dandelion"},{39,"brown_mushroom"},{40,"red_mushroom"},{41,"gold_block"},{42,"iron_block"},{45,"bricks"},{47,"bookshelf"},{48,"mossy_cobblestone"},{49,"obsidian"},{50,"torch"},{52,"spawner"},{54,"chest"},{56,"diamond_ore"},{57,"diamond_block"},{58,"crafting_table"},{60,"farmland"},{61,"furnace"},{62,"furnace"},{79,"ice"},{80,"snow_block"},{81,"cactus"},{82,"clay"},{83,"sugar_cane"},{85,"oak_fence"},{87,"netherrack"},{88,"soul_sand"},{89,"glowstone"},{101,"iron_bars"},{102,"glass_pane"},{103,"melon"},{110,"mycelium"},{111,"lily_pad"},{112,"nether_bricks"},{113,"nether_brick_fence"},{121,"end_stone"},{129,"emerald_ore"},{133,"emerald_block"},{152,"redstone_block"},{153,"nether_quartz_ore"},{165,"slime_block"},{169,"sea_lantern"},{170,"hay_block"},{172,"terracotta"},{173,"coal_block"},{174,"packed_ice"},{179,"red_sandstone"},{206,"end_stone_bricks"}};
  static readonly Dictionary<int,string> Stairs=new Dictionary<int,string>{{53,"oak"},{67,"cobblestone"},{108,"brick"},{109,"stone_brick"},{114,"nether_brick"},{128,"sandstone"},{134,"spruce"},{135,"birch"},{136,"jungle"},{156,"quartz"},{163,"acacia"},{164,"dark_oak"},{180,"red_sandstone"},{203,"purpur"}};
  static BlockState State(string name,params string[] properties){var p=new Dictionary<string,string>();for(int i=0;i<properties.Length;i+=2)p.Add(properties[i],properties[i+1]);return new BlockState(name,p);}
  public static BlockState Resolve(int id,int data){
   if(Stairs.TryGetValue(id,out string stair))return State(stair+"_stairs","facing",new[]{"east","west","south","north"}[data&3],"half",(data&4)!=0?"top":"bottom","shape","straight","waterlogged","false");
   if(id==5)return State(Woods[(data&7)%6]+"_planks");
   if(id==1)return State(new[]{"stone","granite","polished_granite","diorite","polished_diorite","andesite","polished_andesite"}[data%7]);
   if(id==3)return State(data==1?"coarse_dirt":data==2?"podzol":"dirt");
   if(id==17||id==162){string wood=Woods[id==17?(data&3):4+(data&1)];return State(wood+((data&12)==12?"_wood":"_log"),"axis",(data&12)==4?"x":(data&12)==8?"z":"y");}
   if(id==18||id==161)return State(Woods[id==18?data&3:4+(data&1)]+"_leaves","persistent","true","distance","1");
   if(id==35||id==95||id==159||id==160||id==171||id==251||id==252)return State(Colors[data]+(id==35?"_wool":id==95?"_stained_glass":id==159?"_terracotta":id==160?"_stained_glass_pane":id==171?"_carpet":id==251?"_concrete":"_concrete_powder"));
   if(id==43||id==44||id==125||id==126){string material=id>=125?Woods[(data&7)%6]:new[]{"stone","sandstone","petrified_oak","cobblestone","brick","stone_brick","nether_brick","quartz"}[data&7];return State(material+"_slab","type",id==43||id==125?"double":(data&8)!=0?"top":"bottom","waterlogged","false");}
   if(id==31)return State(data==2?"fern":data==0?"dead_bush":"grass");
   if(id==38)return State(new[]{"poppy","blue_orchid","allium","azure_bluet","red_tulip","orange_tulip","white_tulip","pink_tulip","oxeye_daisy"}[data%9]);
   if(id==98)return State(new[]{"stone_bricks","mossy_stone_bricks","cracked_stone_bricks","chiseled_stone_bricks"}[data&3]);
   if(id==78)return State("snow","layers",(1+(data&7)).ToString());
   if(Simple.TryGetValue(id,out string name))return State(name);
   return State("legacy:block_"+id,"data",data.ToString());
  }
 }
}

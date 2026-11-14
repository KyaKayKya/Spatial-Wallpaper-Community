using System;
using Newtonsoft.Json;
using System.ComponentModel;
namespace SpatialWallpaper.Blueprint {
 [Serializable] public sealed class BlueprintSettings {
  static readonly string[] Looks={"clear","natural","warm"},Weathers={"clear","cloudy","rain","snow"},Terrains={"none","grass","waterfront","island","snow"},Times={"fixed","cycle","system"};
  public int Version=1,Quality=1,FrameRate=60;
  [DefaultValue(1),JsonProperty(DefaultValueHandling=DefaultValueHandling.Populate)] public int LayoutVersion=2;
  public string Look="warm",Weather="clear",Terrain="none",TimeMode="fixed";
  public float Hour=17.5f,OffsetHours=0,CycleMinutes=20,WeatherIntensity=.5f,Wind=.35f,WaterLevel=-.025f;
  public float Scale=1,Yaw=0,OffsetX=0,OffsetY=0,OffsetZ=0;
  public bool WaterMotion=true,LeavesMotion=true,Smoke=true,Fireflies=false,WarmWindows=true;
  public BlueprintSettings Clone()=>(BlueprintSettings)MemberwiseClone();
  static void Range(float value,float low,float high,string name){if(float.IsNaN(value)||float.IsInfinity(value)||value<low||value>high)throw new BlueprintImportException("invalid-setting: "+name);}
  static void Option(string value,string name,params string[] choices){if(Array.IndexOf(choices,value)<0)throw new BlueprintImportException("invalid-setting: "+name);}
  public void Validate(){
   if(Version!=1||LayoutVersion<1||LayoutVersion>2||Quality<0||Quality>2||FrameRate!=15&&FrameRate!=30&&FrameRate!=60)throw new BlueprintImportException("invalid-setting: version or quality");
   Option(Look,"look",Looks);Option(Weather,"weather",Weathers);Option(Terrain,"terrain",Terrains);Option(TimeMode,"time",Times);
   Range(Hour,0,24,"hour");Range(OffsetHours,-24,24,"time-offset");Range(CycleMinutes,1,1440,"cycle-minutes");Range(WeatherIntensity,0,1,"weather-intensity");Range(Wind,0,1,"wind");Range(WaterLevel,-.3f,.3f,"water-level");
   Range(Scale,.1f,5,"scale");Range(Yaw,-3600,3600,"yaw");Range(OffsetX,-2,2,"offset-x");Range(OffsetY,-1,1,"offset-y");Range(OffsetZ,-.2f,5,"offset-z");
  }
  public static BlueprintSettings ReadJson(string json){if(json==null||json.Length>16384)throw new BlueprintImportException("invalid-setting: document budget");try{var value=JsonConvert.DeserializeObject<BlueprintSettings>(json,new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,TypeNameHandling=TypeNameHandling.None,MaxDepth=8});if(value==null)throw new BlueprintImportException("invalid-setting: empty");value.Validate();return value;}catch(JsonException){throw new BlueprintImportException("invalid-setting: document");}}
  public string ToJson(){Validate();return JsonConvert.SerializeObject(this,Formatting.None);}
 }
}

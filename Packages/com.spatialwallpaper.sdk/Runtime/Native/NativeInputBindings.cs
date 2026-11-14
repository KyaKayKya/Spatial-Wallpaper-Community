using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native {
 // Reuses the original epoch/freshness/cumulative-edge consumer. The source is
 // a trusted derived snapshot; this host has no hooks or system input handles.
 public sealed class NativeInputBindings:IVmHost {
  readonly InputFrames frames=new InputFrames();readonly Func<long> clock;bool eventMode;uint pressed,released;float wheel;
  static readonly HashSet<string> members=new HashSet<string>(new[]{
   "SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetButtonUp(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single",
   "SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single",
   "SpatialWallpaper.Sdk::Input::GetWheel()->System.Single"
  },StringComparer.Ordinal);
  public static bool SupportsMember(string member)=>members.Contains(member);
  public bool Supports(string member)=>SupportsMember(member);
  public NativeInputBindings(OwnedDomain domain,Func<long> machineClock){clock=machineClock??throw new ArgumentNullException(nameof(machineClock));new ManagedReservation(domain.Ledger,2048).Bind(this);}
  static int Button(string name){switch(name){case "Primary":return 0;case "Secondary":return 1;case "Space":return 2;case "Left":return 3;case "Right":return 4;case "Up":return 5;case "Down":return 6;default:throw new ArgumentException("input-button-name");}}
  public void Advance(TickInput tick){
   var data=tick.Inputs;frames.Advance(data.Length==0?null:data,clock(),data.Length!=0);eventMode=tick.Events.Count>0;pressed=released=0;wheel=0;
   if(frames.Focused)foreach(var edge in tick.Events){if(edge.Kind=="down")pressed|=1u<<Button(edge.Button);else if(edge.Kind=="up")released|=1u<<Button(edge.Button);else wheel+=edge.Value;}
  }
  public object Resolve(string type,JToken value,int scope)=>throw new NotSupportedException("native-input-has-no-object-references");
  public object Call(string member,object receiver,object[] arguments){switch(member){
   case "SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean":return frames.GetButton((string)arguments[0]);
   case "SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean":return eventMode?(pressed&(1u<<Button((string)arguments[0])))!=0:frames.GetButtonDown((string)arguments[0]);
   case "SpatialWallpaper.Sdk::Input::GetButtonUp(System.String)->System.Boolean":return eventMode?(released&(1u<<Button((string)arguments[0])))!=0:frames.GetButtonUp((string)arguments[0]);
   case "SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean":return frames.Focused;
   case "SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single":return frames.X;
   case "SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single":return frames.Y;
   case "SpatialWallpaper.Sdk::Input::GetWheel()->System.Single":return eventMode?wheel:frames.Wheel;
   default:throw new NotSupportedException("native-input-member-not-installed");
  }}
 }
}

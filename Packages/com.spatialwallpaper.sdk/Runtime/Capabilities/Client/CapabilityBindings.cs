using System;using System.Collections.Generic;
namespace SpatialWallpaper.Sdk.Capabilities {
 public sealed class CapabilityBindings {
  readonly CapabilityCalls calls;
  internal SoundMixer Sound=>calls.Sound;
  static readonly HashSet<string> Members=new HashSet<string>(new[]{
   "SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetButtonUp(System.String)->System.Boolean",
   "SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean",
   "SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single",
   "SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single",
   "SpatialWallpaper.Sdk::Input::GetWheel()->System.Single",
   "SpatialWallpaper.Sdk::Audio::GetBand(System.Int32)->System.Single",
   "SpatialWallpaper.Sdk::Storage::BeginRead(System.String)->System.Int32",
   "SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32",
   "SpatialWallpaper.Sdk::Storage::State(System.Int32)->System.Int32",
   "SpatialWallpaper.Sdk::Storage::Version(System.Int32)->System.String",
   "SpatialWallpaper.Sdk::Storage::Error(System.Int32)->System.String",
   "SpatialWallpaper.Sdk::Storage::TakeText(System.Int32)->System.String",
   "SpatialWallpaper.Sdk::Storage::Release(System.Int32)->System.Boolean",
   "SpatialWallpaper.Sdk::Llm::BeginPrompt(System.String)->System.Int32",
   "SpatialWallpaper.Sdk::Llm::BeginCancel(System.Int32)->System.Int32",
   "SpatialWallpaper.Sdk::Llm::State(System.Int32)->System.Int32",
   "SpatialWallpaper.Sdk::Llm::Error(System.Int32)->System.String",
   "SpatialWallpaper.Sdk::Llm::TakeText(System.Int32)->System.String",
   "SpatialWallpaper.Sdk::Llm::Release(System.Int32)->System.Boolean"
  },StringComparer.Ordinal);
  public CapabilityBindings(CapabilityCalls state){calls=state??throw new ArgumentNullException(nameof(state));}
  public static bool Supports(string member)=>Members.Contains(member);
  public object Call(string member,object[] args){switch(member){
   case "SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean":return calls.InputButton((string)args[0]);
   case "SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean":return calls.InputDown((string)args[0]);
   case "SpatialWallpaper.Sdk::Input::GetButtonUp(System.String)->System.Boolean":return calls.InputUp((string)args[0]);
   case "SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean":return calls.InputFocused();
   case "SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single":return calls.InputX();
   case "SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single":return calls.InputY();
   case "SpatialWallpaper.Sdk::Input::GetWheel()->System.Single":return calls.InputWheel();
   case "SpatialWallpaper.Sdk::Audio::GetBand(System.Int32)->System.Single":return calls.AudioBand((int)args[0]);
   case "SpatialWallpaper.Sdk::Storage::BeginRead(System.String)->System.Int32":return calls.BeginRead((string)args[0]);
   case "SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32":return calls.BeginWriteText((string)args[0],(string)args[1],(string)args[2]);
   case "SpatialWallpaper.Sdk::Storage::State(System.Int32)->System.Int32":return calls.State((int)args[0]);
   case "SpatialWallpaper.Sdk::Storage::Version(System.Int32)->System.String":return calls.Version((int)args[0]);
   case "SpatialWallpaper.Sdk::Storage::Error(System.Int32)->System.String":return calls.Error((int)args[0]);
   case "SpatialWallpaper.Sdk::Storage::TakeText(System.Int32)->System.String":return calls.TakeText((int)args[0]);
   case "SpatialWallpaper.Sdk::Storage::Release(System.Int32)->System.Boolean":return calls.Release((int)args[0]);
   case "SpatialWallpaper.Sdk::Llm::BeginPrompt(System.String)->System.Int32":return calls.BeginPrompt((string)args[0]);
   case "SpatialWallpaper.Sdk::Llm::BeginCancel(System.Int32)->System.Int32":return calls.BeginCancel((int)args[0]);
   case "SpatialWallpaper.Sdk::Llm::State(System.Int32)->System.Int32":return calls.State((int)args[0]);
   case "SpatialWallpaper.Sdk::Llm::Error(System.Int32)->System.String":return calls.Error((int)args[0]);
   case "SpatialWallpaper.Sdk::Llm::TakeText(System.Int32)->System.String":return calls.TakeText((int)args[0]);
   case "SpatialWallpaper.Sdk::Llm::Release(System.Int32)->System.Boolean":return calls.Release((int)args[0]);
   default:throw new InvalidOperationException("capability-member-not-bound");
  }}
 }
}

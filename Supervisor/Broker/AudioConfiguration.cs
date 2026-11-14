using System;using System.IO;using System.Linq;using System.Globalization;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;
namespace SpatialWallpaper.Supervision.Broker {
 // Trusted platform selection. No source selector is present in the author protocol.
 public sealed class AudioConfiguration {
  readonly int process;readonly ulong created;
  AudioConfiguration(int pid,ulong creation){process=pid;created=creation;}
  public static AudioConfiguration Parse(JToken token){
   if(token==null||token.Type==JTokenType.Null)return null;
   var value=token as JObject;if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"created","process"})||value["process"].Type!=JTokenType.Integer||value["created"].Type!=JTokenType.String)throw new InvalidDataException("audio-configuration-fields");
   long pid=(long)value["process"];ulong creation;string text=(string)value["created"];
   if(pid<1||pid>int.MaxValue||text.Length>20||!ulong.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out creation)||creation==0||creation.ToString(CultureInfo.InvariantCulture)!=text)throw new InvalidDataException("audio-source-identity");
   return new AudioConfiguration((int)pid,creation);
  }
  public AudioBroker Open(PrivateStation station,Action<Exception> cleanupFailure=null)=>new AudioBroker(station,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AudioSampler.exe"),process,created,cleanupFailure);
 }
}

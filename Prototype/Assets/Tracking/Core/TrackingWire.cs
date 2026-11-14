using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 public static class QpcClock {
  [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long counter);
  [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long frequency);
  [StructLayout(LayoutKind.Sequential)] struct Timebase { public uint Numer,Denom; }
  [DllImport("/usr/lib/libSystem.B.dylib")] static extern ulong mach_absolute_time();
  [DllImport("/usr/lib/libSystem.B.dylib")] static extern int mach_timebase_info(out Timebase info);
  static bool Mac=>RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
  public static string Domain=>Mac?"macos_mach_absolute_us_v1":"windows_qpc_us_v1";
  static readonly long frequency=Frequency();
  static readonly Timebase timebase=Base();
  static Timebase Base(){if(!Mac)return default(Timebase);if(mach_timebase_info(out var t)!=0||t.Denom==0)throw new InvalidOperationException("Mach clock unavailable");return t;}
  static long Frequency(){if(Mac)return 0;if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))throw new PlatformNotSupportedException("Tracking clock supports Windows/macOS");long f;if(!QueryPerformanceFrequency(out f)||f<=0)throw new InvalidOperationException("QPC unavailable");return f;}
  public static ulong Now {get{
   if(Mac){ulong t=mach_absolute_time(),d=(ulong)timebase.Denom*1000;return checked(t/d*timebase.Numer+t%d*timebase.Numer/d);}
   long q;if(!QueryPerformanceCounter(out q))throw new InvalidOperationException("QPC unavailable");return checked((ulong)(q/frequency)*1000000+(ulong)(q%frequency)*1000000/(ulong)frequency);
  }}
 }
 public static class TrackingWire {
  public const int MaxFrame=65536;
  public static ulong Decimal(JToken value){
   if(value==null||value.Type!=JTokenType.String||!Regex.IsMatch((string)value,@"\A(0|[1-9][0-9]{0,19})\z"))throw new InvalidDataException("INVALID_DECIMAL");
   ulong result;if(!ulong.TryParse((string)value,out result))throw new InvalidDataException("INVALID_DECIMAL");return result;
  }
  public static string Id(JToken value){Guid id;if(value==null||value.Type!=JTokenType.String||!Guid.TryParseExact((string)value,"D",out id))throw new InvalidDataException("INVALID_ID");return (string)value;}
  public static string Text(JToken value){if(value==null||value.Type!=JTokenType.String||((string)value).Length==0)throw new InvalidDataException("INVALID_STRING");return (string)value;}
  public static double Number(JToken value){
   if(value==null||(value.Type!=JTokenType.Float&&value.Type!=JTokenType.Integer))throw new InvalidDataException("INVALID_NUMBER");
   double result=(double)value;if(double.IsNaN(result)||double.IsInfinity(result))throw new InvalidDataException("NONFINITE_NUMBER");return result;
  }
  public static double[] Vector(JToken value,int count){
   var a=value as JArray;if(a==null||a.Count!=count)throw new InvalidDataException("INVALID_VECTOR");
   var result=new double[count];for(int i=0;i<count;i++)result[i]=Number(a[i]);return result;
  }
  public static JObject Decode(byte[] raw){
   if(raw.Length<1||raw.Length>MaxFrame)throw new InvalidDataException("FRAME_LENGTH");
   string json=new UTF8Encoding(false,true).GetString(raw);
   new Grammar(json).Check();
   using(var reader=new JsonTextReader(new StringReader(json)){MaxDepth=16,DateParseHandling=DateParseHandling.None}){
    var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
    foreach(var item in result.Descendants())if(item.Type==JTokenType.Float)Number(item);
    return result;
   }
  }
  public static JObject Read(Stream stream){
   var prefix=Exact(stream,4);
   uint size=((uint)prefix[0]<<24)|((uint)prefix[1]<<16)|((uint)prefix[2]<<8)|prefix[3];
   if(size<1||size>MaxFrame)throw new InvalidDataException("FRAME_LENGTH");
   return Decode(Exact(stream,(int)size));
  }
  static byte[] Exact(Stream stream,int size){
   var buffer=new byte[size];int offset=0;
   while(offset<size){int read=stream.Read(buffer,offset,size-offset);if(read==0)throw new EndOfStreamException();offset+=read;}return buffer;
  }
  public static void Write(Stream stream,JObject message){
   byte[] raw=Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
   if(raw.Length<1||raw.Length>MaxFrame)throw new InvalidDataException("FRAME_LENGTH");
   uint size=(uint)raw.Length;stream.Write(new[]{(byte)(size>>24),(byte)(size>>16),(byte)(size>>8),(byte)size},0,4);stream.Write(raw,0,raw.Length);
  }
  // Json.NET intentionally accepts JavaScript syntax. The wire contract only accepts JSON.
  sealed class Grammar {
   readonly string s;int i;
   public Grammar(string value){s=value;}
   void Fail(){throw new InvalidDataException("INVALID_JSON");}
   void White(){while(i<s.Length&&(s[i]==' '||s[i]=='\r'||s[i]=='\n'||s[i]=='\t'))i++;}
   bool Take(char c){White();if(i<s.Length&&s[i]==c){i++;return true;}return false;}
   void Need(char c){if(!Take(c))Fail();}
   public void Check(){White();if(i==s.Length||s[i]!='{')Fail();Value(0);White();if(i!=s.Length)Fail();}
   void Value(int depth){
    if(depth>16)Fail();White();if(i>=s.Length)Fail();char c=s[i];
    if(c=='{'){i++;if(Take('}'))return;do{String();Need(':');Value(depth+1);}while(Take(','));Need('}');}
    else if(c=='['){i++;if(Take(']'))return;do{Value(depth+1);}while(Take(','));Need(']');}
    else if(c=='"')String();
    else if(c=='t')Literal("true");else if(c=='f')Literal("false");else if(c=='n')Literal("null");
    else {
     int start=i;if(s[i]=='-')i++;Digits(true);
     if(i<s.Length&&s[i]=='.'){i++;Digits(false);}
     if(i<s.Length&&(s[i]=='e'||s[i]=='E')){i++;if(i<s.Length&&(s[i]=='+'||s[i]=='-'))i++;Digits(false);}
     if(i==start)Fail();
    }
   }
   void Digits(bool leading){
    int start=i;if(leading&&i<s.Length&&s[i]=='0'){i++;return;}
    while(i<s.Length&&s[i]>='0'&&s[i]<='9')i++;if(i==start)Fail();
   }
   void Literal(string text){if(i+text.Length>s.Length||s.Substring(i,text.Length)!=text)Fail();i+=text.Length;}
   void String(){
    Need('"');while(i<s.Length){
     char c=s[i++];if(c=='"')return;if(c<32)Fail();
     if(c=='\\'){if(i>=s.Length)Fail();char e=s[i++];if(e=='u'){for(int n=0;n<4;n++){if(i>=s.Length||!Uri.IsHexDigit(s[i++]))Fail();}}
      else if("\"\\/bfnrt".IndexOf(e)<0)Fail();}
    }Fail();
   }
  }
 }
}

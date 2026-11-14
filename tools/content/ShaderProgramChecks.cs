using System;
using System.Collections.Generic;
using System.IO;
using SpatialWallpaper.Sdk;

static class ShaderProgramChecks {
 public static void Run(string fixture,Action<string,Action> check,Action<bool> assert){
  if(!File.Exists(fixture))throw new InvalidDataException("missing-real-shader-program-fixture");
  var data=File.ReadAllBytes(fixture);
  Action<byte[]> validate=bytes=>ShaderProgramData.Validate(new[]{bytes},new HashSet<int>{1,3},new HashSet<int>{0,2});
  Action<byte[]> reject=bytes=>{try{validate(bytes);}catch(InvalidDataException){return;}throw new Exception("inner payload accepted");};
  check("real-inner-shader-program",()=>validate(data));
  check("reject-inner-entry-count",()=>{var b=(byte[])data.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),b,4);reject(b);});
  check("reject-inner-entry-offset",()=>{var b=(byte[])data.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),0,b,4,4);reject(b);});
  check("reject-inner-parameter-group-count",()=>{var b=(byte[])data.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),0,b,56,4);reject(b);});
  check("reject-inner-version",()=>{var b=(byte[])data.Clone();Array.Copy(BitConverter.GetBytes(1),0,b,52,4);reject(b);});
  check("reject-inner-unregistered-program-index",()=>{try{ShaderProgramData.Validate(new[]{data},new HashSet<int>{int.MaxValue},new HashSet<int>{0,2});}catch(InvalidDataException){return;}throw new Exception("program index accepted");});
  check("reject-inner-aggregate-parameter-expansion",()=>{
   byte[] parameters;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(202012090);writer.Write(4096);for(int i=0;i<4096;i++){writer.Write(0);writer.Write(65536);writer.Write(0);writer.Write(0);}writer.Write(0);parameters=stream.ToArray();}
   int n=BitConverter.ToInt32(data,0);var bodies=new List<byte[]>();for(int i=0;i<n;i++){int offset=BitConverter.ToInt32(data,4+i*12),length=BitConverter.ToInt32(data,8+i*12);var body=new byte[length];Array.Copy(data,offset,body,0,length);bodies.Add(i==0?parameters:body);}
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(n);int offset=4+n*12;foreach(var body in bodies){writer.Write(offset);writer.Write(body.Length);writer.Write(0);offset+=body.Length;}foreach(var body in bodies)writer.Write(body);reject(stream.ToArray());}
  });
 }
}

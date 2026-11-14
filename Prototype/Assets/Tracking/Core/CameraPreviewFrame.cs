using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 public sealed class CameraPreviewFrame {
  public readonly byte[] Jpeg;public readonly int Width,Height;
  public readonly ulong Captured,Frame;public readonly string Stream;
  public readonly bool FaceFound;public readonly double InferenceMs;public readonly double? IpdPixels;
  public CameraPreviewFrame(JObject data){
   Stream=TrackingWire.Id(data["stream_id"]);Frame=TrackingWire.Decimal(data["frame_id"]);Captured=TrackingWire.Decimal(data["captured_at_us"]);
   if(data["width"]?.Type!=JTokenType.Integer||data["height"]?.Type!=JTokenType.Integer||data["face_found"]?.Type!=JTokenType.Boolean||data["mirrored"]?.Type!=JTokenType.Boolean||(bool)data["mirrored"]!=true)throw new InvalidDataException("INVALID_PREVIEW");
   Width=(int)data["width"];Height=(int)data["height"];FaceFound=(bool)data["face_found"];
   InferenceMs=TrackingWire.Number(data["inference_ms"]);IpdPixels=data["ipd_px"]?.Type==JTokenType.Null?(double?)null:TrackingWire.Number(data["ipd_px"]);
   string encoded=TrackingWire.Text(data["jpeg"]);
   if(Width<1||Width>640||Height<1||Height>480||InferenceMs<0||InferenceMs>60000||IpdPixels.HasValue&&(IpdPixels<1||IpdPixels>32768)||encoded.Length>53336)throw new InvalidDataException("INVALID_PREVIEW");
   try{Jpeg=Convert.FromBase64String(encoded);}catch(FormatException){throw new InvalidDataException("INVALID_PREVIEW_JPEG");}
   if(Jpeg.Length<4||Jpeg.Length>40000||Jpeg[0]!=255||Jpeg[1]!=216||Jpeg[Jpeg.Length-2]!=255||Jpeg[Jpeg.Length-1]!=217)throw new InvalidDataException("INVALID_PREVIEW_JPEG");
   bool dimensions=false;
   for(int i=2;i+3<Jpeg.Length;){
    if(Jpeg[i]!=255)break;int marker=Jpeg[i+1];
    if(marker==218||marker==217)break;
    int length=(Jpeg[i+2]<<8)|Jpeg[i+3];if(length<2||i+2+length>Jpeg.Length)break;
    if(marker==192||marker==193||marker==194){
     if(length<8)break;
     dimensions=((Jpeg[i+5]<<8)|Jpeg[i+6])==Height&&((Jpeg[i+7]<<8)|Jpeg[i+8])==Width;break;
    }
    i+=length+2;
   }
   if(!dimensions)throw new InvalidDataException("INVALID_PREVIEW_DIMENSIONS");
  }
 }
}

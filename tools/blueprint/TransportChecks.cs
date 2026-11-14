using System;
using System.IO;
using System.Threading;
using SpatialWallpaper.Blueprint;
class TransportChecks {
 static int Main(string[] args){try{var document=BlueprintReader.Read(File.ReadAllBytes(args[0]),"sample.zip",CancellationToken.None);byte[] bytes=BlueprintDocumentTransport.Write(document);var copy=BlueprintDocumentTransport.Read(bytes);if(copy.SourceHash!=document.SourceHash||copy.Width!=document.Width||copy.Height!=document.Height||copy.Length!=document.Length||copy.BlockCount!=document.BlockCount)throw new Exception("document identity changed");for(int i=0;i<document.Indices.Length;i++)if(document.Palette[document.Indices[i]].Key!=copy.Palette[copy.Indices[i]].Key)throw new Exception("block state changed");Console.WriteLine("PASS worker-document-roundtrip");bool rejected=false;try{BlueprintDocumentTransport.Read(new byte[]{1,2,3});}catch(BlueprintImportException){rejected=true;}if(!rejected)throw new Exception("truncated document accepted");Console.WriteLine("PASS worker-document-rejects-truncation");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}

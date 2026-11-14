#if CONTENT_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.ContentValidation
{
    // Run in the standalone probe as well as the Mono boundary-check runner.
    public static class ContentProbeChecks
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            string valid = "{\"engine\":\"2022.3.62f3\",\"pipeline\":\"14.0.12\",\"nodes\":[{\"kind\":\"cube\",\"position\":[0,0,2],\"scale\":1,\"color\":[1,0.5,0,1]}]}";
            Check(ProbeData.TryValidate(valid, out _), "positive-data", failures);
            var unknown = JObject.Parse(valid);
            unknown["nodes"][0]["behavior"] = "HiddenProbe";
            Check(!ProbeData.TryValidate(unknown.ToString(), out _), "hidden-behavior", failures);
            var nested = JObject.Parse(valid);
            nested["nodes"][0]["children"] = new JArray(new JObject { ["kind"] = "MonoBehaviour" });
            Check(!ProbeData.TryValidate(nested.ToString(), out _), "nested-hidden-object", failures);
            var events = JObject.Parse(valid);
            events["nodes"][0]["animationEvents"] = new JArray("Callback");
            Check(!ProbeData.TryValidate(events.ToString(), out _), "animation-event", failures);
            var reference = JObject.Parse(valid);
            reference["nodes"][0]["cookie"] = "file:///outside";
            Check(!ProbeData.TryValidate(reference.ToString(), out _), "dangerous-known-config", failures);
            var code = JObject.Parse(valid);
            code["dependencies"] = new JArray("hidden.dll");
            Check(!ProbeData.TryValidate(code.ToString(), out _), "code-dependency", failures);
            var version = JObject.Parse(valid);
            version["engine"] = "2022.3.58f1";
            Check(!ProbeData.TryValidate(version.ToString(), out _), "engine-mismatch", failures);
            version = JObject.Parse(valid);
            version["pipeline"] = "HDRP";
            Check(!ProbeData.TryValidate(version.ToString(), out _), "pipeline-mismatch", failures);
            var invalid = JObject.Parse(valid);
            invalid["nodes"][0]["scale"] = -1;
            Check(!ProbeData.TryValidate(invalid.ToString(), out _), "invalid-scale", failures);
            Check(!ProbeData.TryValidate(valid.Replace("\"engine\":", "\"engine\":\"bad\",\"engine\":"), out _), "duplicate-key", failures);
            Check(!ProbeData.TryValidate("{\"$type\":\"System.Object\"}", out _), "type-injection", failures);
            var wrongType = JObject.Parse(valid);
            wrongType["engine"] = new JObject();
            Check(!ProbeData.TryValidate(wrongType.ToString(), out _), "engine-object-instead-of-string", failures);
            return failures;
        }

        static void Check(bool condition, string name, List<string> failures)
        {
            Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
            if (!condition) failures.Add(name);
        }

        public static List<string> CheckBundles(string root)
        {
            var failures = new List<string>();
            var native = File.ReadAllBytes(Path.Combine(root, "bundles", "resources"));
            Check(NativeTypeTableProbe.TryInspect(native, out _), "native-type-table-observation", failures);
            var marker = Encoding.ASCII.GetBytes("archive:/");
            int streamOffset = -1;
            for (int i = 0; i <= native.Length - marker.Length; i++)
            {
                bool matches = true;
                for (int j = 0; j < marker.Length; j++) if (native[i + j] != marker[j]) matches = false;
                if (matches) { streamOffset = i; break; }
            }
            var externalStream = (byte[])native.Clone();
            if (streamOffset >= 0) Array.Copy(Encoding.ASCII.GetBytes("file:///X"), 0, externalStream, streamOffset, marker.Length);
            Check(streamOffset >= 0 && NativeTypeTableProbe.TryInspect(externalStream, out _), "observation-type-table-misses-stream-config", failures);
            Check(!ProbeData.TryValidate(Encoding.UTF8.GetString(externalStream), out _), "data-route-rejects-native-stream-config", failures);
            Check(!NativeTypeTableProbe.TryInspect(File.ReadAllBytes(Path.Combine(root, "bundles", "hidden-resource")), out _), "script-type-table-rejection", failures);
            Check(!NativeTypeTableProbe.TryInspect(File.ReadAllBytes(Path.Combine(root, "bundles", "callbacks")), out _), "scene-type-table-rejection", failures);
            native[0] = 0;
            Check(!NativeTypeTableProbe.TryInspect(native, out _), "tampered-type-table-container", failures);
            Check(!NativeTypeTableProbe.TryInspect(new byte[] { 77, 90, 0, 0 }, out _), "non-bundle-type-table-input", failures);
            return failures;
        }

        public static int Main(string[] args)
        {
            int failures = Run().Count;
            if (args.Length > 0) failures += CheckBundles(args[0]).Count;
            return failures == 0 ? 0 : 1;
        }
    }
}

#endif

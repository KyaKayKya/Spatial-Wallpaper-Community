#if CONTENT_PROBE
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.ContentValidation
{
    public static class ProbeData
    {
        // Probe-only data subset. No Unity object is parsed or created here.
        // It deliberately has no type names, object references or event dispatch.
        public static bool TryValidate(string json, out string reason)
        {
            reason = "invalid-json";
            if (json == null || json.Length > 65536) return false;
            JObject document;
            try
            {
                document = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException error)
            {
                reason = "invalid-json: " + error.Message;
                return false;
            }
            reason = "unsupported-field";
            if (!Keys(document, "engine", "pipeline", "nodes")) return false;
            reason = "engine-mismatch";
            if (document["engine"].Type != JTokenType.String || (string)document["engine"] != "2022.3.62f3") return false;
            reason = "pipeline-mismatch";
            if (document["pipeline"].Type != JTokenType.String || (string)document["pipeline"] != "14.0.12") return false;
            reason = "invalid-nodes";
            if (!(document["nodes"] is JArray nodes) || nodes.Count == 0 || nodes.Count > 64) return false;
            foreach (var value in nodes)
            {
                if (!(value is JObject node) || !Keys(node, "kind", "position", "scale", "color")) return false;
                if (node["kind"].Type != JTokenType.String || (string)node["kind"] != "cube") return false;
                if (!Vector(node["position"], 3, -10, 10) || !Number(node["scale"], .001, 10) || !Vector(node["color"], 4, 0, 1)) return false;
            }
            reason = "";
            return true;
        }

        static bool Keys(JObject value, params string[] keys)
        {
            return value.Properties().Count() == keys.Length && keys.All(k => value[k] != null);
        }

        static bool Vector(JToken token, int length, double min, double max)
        {
            return token is JArray array && array.Count == length && array.All(v => Number(v, min, max));
        }

        static bool Number(JToken token, double min, double max)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) return false;
            double value = (double)token;
            return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max;
        }
    }
}

#endif

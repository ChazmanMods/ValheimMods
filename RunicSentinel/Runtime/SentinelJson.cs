using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    // Use the serializer shipped with Valheim for transport DTOs, independently of
    // Unity's native serializer and its client/headless type metadata.
    internal static class SentinelJson
    {
        private static JsonSerializerSettings Settings()=>new JsonSerializerSettings {
            MaxDepth=20, TypeNameHandling=TypeNameHandling.None,
            Converters={new PositionConverter()}
        };
        internal static string Write(object value,bool pretty=false)=>JsonConvert.SerializeObject(value,pretty?Formatting.Indented:Formatting.None,Settings());
        internal static T Read<T>(string value)
        {
            if(string.IsNullOrEmpty(value)||value.Length>120*1024)throw new FormatException("Empty or oversized dashboard response.");
            return JsonConvert.DeserializeObject<T>(value,Settings());
        }
        private sealed class PositionConverter:JsonConverter
        {
            public override bool CanConvert(Type type)=>type==typeof(Vector3);
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                var p=(Vector3)value;writer.WriteStartObject();
                writer.WritePropertyName("x");writer.WriteValue(p.x);
                writer.WritePropertyName("y");writer.WriteValue(p.y);
                writer.WritePropertyName("z");writer.WriteValue(p.z);writer.WriteEndObject();
            }
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)
            {var p=JObject.Load(reader);return new Vector3((float?)p["x"]??0,(float?)p["y"]??0,(float?)p["z"]??0);}
        }
    }
}

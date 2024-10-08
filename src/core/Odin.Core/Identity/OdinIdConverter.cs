﻿using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odin.Core.Identity
{
    public class OdinIdConverter : JsonConverter<OdinId>
    {
        public override OdinId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            return new OdinId(value);
        }
        
        
        public override void Write(Utf8JsonWriter writer, OdinId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

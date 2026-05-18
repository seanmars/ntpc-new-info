using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApi.Json;

public sealed class UInt64StringJsonConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s)) return 0UL;
            return ulong.Parse(s, CultureInfo.InvariantCulture);
        }
        return reader.GetUInt64();
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

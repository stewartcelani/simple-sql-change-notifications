using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleSqlChangeNotifications.Helpers;

public class ObjectDeserializer : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var type = reader.TokenType;

        if (type == JsonTokenType.Number)
        {
            var oki = reader.TryGetInt32(out var vali);
            if (oki)
            {
                return vali;
            }
            var okl = reader.TryGetInt64(out var vall);
            if (okl)
            {
                return vall;
            }
            var okd = reader.TryGetDouble(out var val);
            if (okd)
            {
                return val;
            }
        }

        if (type == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (type == JsonTokenType.True || type == JsonTokenType.False)
        {
            return reader.GetBoolean();
        }
        // copied from corefx repo:
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
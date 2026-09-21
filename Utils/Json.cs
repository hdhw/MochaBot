using System.Text.Json;

namespace MochaBot.Utils;

public static class Json
{
    public static bool TryProp(this JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }

    public static string? Str(this JsonElement element, string name)
    {
        if (!element.TryProp(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static long Long(this JsonElement element, string name, long fallback = 0)
    {
        if (!element.TryProp(name, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
            return n;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var s))
            return s;
        return fallback;
    }

    public static int Int(this JsonElement element, string name, int fallback = 0) =>
        (int)element.Long(name, fallback);

    public static bool Bool(this JsonElement element, string name, bool fallback = false)
    {
        if (!element.TryProp(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : fallback,
            _ => fallback,
        };
    }

    public static JsonElement? Sub(this JsonElement element, string name) =>
        element.TryProp(name, out var value) ? value : null;

    public static double Double(this JsonElement element, string name, double fallback = 0) =>
        element.TryProp(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)
            ? d
            : fallback;
}

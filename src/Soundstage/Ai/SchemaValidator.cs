using System.Text.Json.Nodes;

namespace Soundstage.Ai;

/// <summary>
/// Checks a reply against the small JSON Schema subset the AI layer uses: type (string, number, integer, boolean, object,
/// array, null, or a list of those), properties, required, enum, items, minimum, maximum, minItems, maxItems. Unknown
/// keywords are ignored. Providers ask for structured output where they can, and this runs regardless (plan 6.1).
/// </summary>
public static class SchemaValidator
{
    /// <summary>Null when the value fits, otherwise a short path-and-reason string.</summary>
    public static string? Validate(JsonNode? value, JsonNode? schema, string path = "$")
    {
        if (schema is not JsonObject s) return null;

        if (s["type"] is { } typeNode)
        {
            var allowed = typeNode is JsonArray arr ? arr.Select(n => n?.GetValue<string>() ?? "").ToList() : new List<string> { typeNode.GetValue<string>() };
            if (!allowed.Any(t => IsType(value, t))) return $"{path}: expected {string.Join(" or ", allowed)}, got {Describe(value)}";
        }

        if (s["enum"] is JsonArray choices && !choices.Any(c => JsonNode.DeepEquals(c, value)))
            return $"{path}: value is not one of the allowed choices";

        if (value is JsonValue v && v.TryGetValue<double>(out var number))
        {
            if (s["minimum"] is { } min && number < min.GetValue<double>()) return $"{path}: below the minimum";
            if (s["maximum"] is { } max && number > max.GetValue<double>()) return $"{path}: above the maximum";
        }

        if (value is JsonObject obj)
        {
            if (s["required"] is JsonArray req)
                foreach (var name in req.Select(r => r?.GetValue<string>()).Where(n => n != null))
                    if (!obj.ContainsKey(name!)) return $"{path}: missing '{name}'";
            if (s["properties"] is JsonObject props)
                foreach (var (name, sub) in props)
                    if (obj.TryGetPropertyValue(name, out var child))
                    {
                        var error = Validate(child, sub, $"{path}.{name}");
                        if (error != null) return error;
                    }
        }

        if (value is JsonArray list)
        {
            if (s["minItems"] is { } mi && list.Count < mi.GetValue<int>()) return $"{path}: too few items";
            if (s["maxItems"] is { } ma && list.Count > ma.GetValue<int>()) return $"{path}: too many items";
            if (s["items"] is JsonObject itemSchema)
                for (var i = 0; i < list.Count; i++)
                {
                    var error = Validate(list[i], itemSchema, $"{path}[{i}]");
                    if (error != null) return error;
                }
        }
        return null;
    }

    private static bool IsType(JsonNode? value, string type) => type switch
    {
        "null" => value == null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue sv && sv.TryGetValue<string>(out _),
        "boolean" => value is JsonValue bv && bv.TryGetValue<bool>(out _),
        "number" => value is JsonValue nv && nv.TryGetValue<double>(out _),
        "integer" => value is JsonValue iv && iv.TryGetValue<double>(out var d) && d == Math.Floor(d),
        _ => true,
    };

    private static string Describe(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject => "an object",
        JsonArray => "an array",
        JsonValue v when v.TryGetValue<string>(out _) => "a string",
        JsonValue v when v.TryGetValue<bool>(out _) => "a boolean",
        _ => "a number",
    };
}

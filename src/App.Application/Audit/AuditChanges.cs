using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace App.Application.Audit;

/// <summary>Builds the <c>{ before, after }</c> JSON stored with an audit entry.</summary>
public static class AuditChanges
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(object? before, object? after)
    {
        var root = new JsonObject { ["before"] = Clean(before), ["after"] = Clean(after) };
        return root.ToJsonString(Options);
    }

    private static JsonNode? Clean(object? value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), Options);
        DropSecrets(node);
        return node;
    }

    private static readonly string[] SecretMarkers = ["password", "secret", "token", "apikey", "api_key", "authorization", "credential"];

    private static bool IsSecretKey(string key) =>
        SecretMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase));

    // Defence in depth: secrets must never reach the log, whatever object a caller passes in.
    private static void DropSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(IsSecretKey).ToList())
                    obj.Remove(key);
                foreach (var (_, child) in obj) DropSecrets(child);
                break;
            case JsonArray arr:
                foreach (var child in arr) DropSecrets(child);
                break;
        }
    }
}

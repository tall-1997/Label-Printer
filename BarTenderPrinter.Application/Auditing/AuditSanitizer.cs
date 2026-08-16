using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BarTenderPrinter.Application.Auditing;

public sealed record AuditActor(string ActorId, string StationId, string ShiftId);

public static class AuditSanitizer
{
    private static readonly string[] SensitiveNames = ["imei", "imei1", "imei2", "imei3", "imei4", "sn", "serialnumber"];
    private static readonly string[] DiagnosticNames = ["diagnostic", "result", "exception", "stacktrace"];

    public static string? Serialize(object? value)
    {
        if (value == null) return null;
        JsonNode? node;
        if (value is string json)
        {
            try { node = JsonNode.Parse(json); }
            catch (JsonException) { node = JsonValue.Create(json); }
        }
        else node = JsonSerializer.SerializeToNode(value);
        Sanitize(node, null);
        return node?.ToJsonString();
    }

    private static void Sanitize(JsonNode? node, string? propertyName)
    {
        if (node == null) return;
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                var name = property.Key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                if (SensitiveNames.Contains(name)) obj[property.Key] = "***";
                else if (DiagnosticNames.Any(name.Contains) && property.Value != null)
                {
                    var raw = property.Value.ToJsonString();
                    obj[property.Key] = new JsonObject
                    {
                        ["redacted"] = true,
                        ["length"] = Encoding.UTF8.GetByteCount(raw),
                        ["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()
                    };
                }
                else Sanitize(property.Value, property.Key);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) Sanitize(item, propertyName);
        }
    }
}

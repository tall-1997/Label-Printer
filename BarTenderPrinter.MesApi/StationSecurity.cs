using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BarTenderPrinter.Persistence;

namespace BarTenderPrinter.MesApi;

public sealed record StationSession(string UserId, string StationId, string ShiftId, IReadOnlySet<string> Roles);

public static class StationSessionAccessor
{
    public static StationSession Get(ClaimsPrincipal principal)
    {
        var userId = Required(principal, ClaimTypes.NameIdentifier);
        var stationId = Required(principal, StationClaimTypes.StationId);
        var shiftId = Required(principal, StationClaimTypes.ShiftId);
        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value.Trim())
            .Where(role => role.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (roles.Count == 0) throw new InvalidOperationException("AUTHENTICATION_CONTEXT_INVALID");
        return new StationSession(userId, stationId, shiftId, roles);
    }

    private static string Required(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirstValue(type)?.Trim() ?? "";
        return value.Length > 0 ? value : throw new InvalidOperationException("AUTHENTICATION_CONTEXT_INVALID");
    }
}

public sealed class StationSessionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Items[typeof(StationSession)] = StationSessionAccessor.Get(context.HttpContext.User);
        return await next(context);
    }
}

public static class AuditSnapshot
{
    private static readonly HashSet<string> RedactedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Imei", "Imei1", "Imei2", "Imei3", "Imei4", "SerialNumber", "Sn"
    };
    private static readonly HashSet<string> SummarizedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Diagnostic", "Diagnostics", "DiagnosticCode", "Result", "ResultJson", "Error", "ErrorMessage",
        "Exception", "StackTrace"
    };

    public static AuditEventSnapshot Create(HttpContext context, string action, string entityType, string entityId,
        object? before, object? after)
    {
        var session = context.Items[typeof(StationSession)] as StationSession ??
            StationSessionAccessor.Get(context.User);
        return new AuditEventSnapshot(Guid.NewGuid().ToString("N"), session.UserId, session.StationId,
            session.ShiftId, context.TraceIdentifier, action, entityType, entityId,
            Serialize(before), Serialize(after), DateTimeOffset.UtcNow);
    }

    public static string? Serialize(object? value)
    {
        if (value == null) return null;
        var node = value is string json ? JsonNode.Parse(json) : JsonSerializer.SerializeToNode(value);
        Redact(node);
        return node?.ToJsonString();
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (RedactedNames.Contains(property.Key)) jsonObject[property.Key] = "***";
                else if (SummarizedNames.Contains(property.Key)) jsonObject[property.Key] = Summary(property.Value);
                else if (property.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var nestedJson))
                {
                    try
                    {
                        var nestedNode = JsonNode.Parse(nestedJson);
                        Redact(nestedNode);
                        jsonObject[property.Key] = nestedNode?.ToJsonString();
                    }
                    catch (JsonException)
                    {
                        // Ordinary strings are already safe to retain.
                    }
                }
                else Redact(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray) Redact(item);
        }
    }

    private static JsonObject Summary(JsonNode? value)
    {
        var serialized = value?.ToJsonString() ?? "null";
        return new JsonObject
        {
            ["redacted"] = true,
            ["length"] = Encoding.UTF8.GetByteCount(serialized),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant()
        };
    }
}

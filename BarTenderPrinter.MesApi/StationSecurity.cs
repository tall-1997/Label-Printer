using System.Security.Claims;
using BarTenderPrinter.Application.Auditing;
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
    public static AuditEventSnapshot Create(HttpContext context, string action, string entityType, string entityId,
        object? before, object? after)
    {
        var session = context.Items[typeof(StationSession)] as StationSession ??
            StationSessionAccessor.Get(context.User);
        return new AuditEventSnapshot(Guid.NewGuid().ToString("N"), session.UserId, session.StationId,
            session.ShiftId, context.TraceIdentifier, action, entityType, entityId,
            AuditSanitizer.Serialize(before), AuditSanitizer.Serialize(after), DateTimeOffset.UtcNow);
    }
}

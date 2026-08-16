using BarTenderPrinter.Application.Identity;

namespace BarTenderPrinter.MesApi;

public static class PlatformEndpoints
{
    public static RouteGroupBuilder MapPlatformEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/session", GetSession);
        return api;
    }

    private static IResult GetSession(HttpContext context)
    {
        var session = context.Items[typeof(StationSession)] as StationSession ??
            StationSessionAccessor.Get(context.User);
        var roles = session.Roles.Order(StringComparer.Ordinal).ToArray();
        return Results.Ok(new PlatformSessionView(session.UserId, session.UserId,
            session.StationId, session.ShiftId, roles, PlatformCapabilities.Resolve(roles)));
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BarTenderPrinter.MesApi;

public static class StationClaimTypes
{
    public const string StationId = "station_id";
    public const string ShiftId = "shift_id";
}

public sealed class StationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StationBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var suppliedToken = header["Bearer ".Length..].Trim();
        foreach (var session in configuration.GetSection("MesSecurity:Sessions").GetChildren())
        {
            var configuredToken = session["Token"] ?? "";
            if (!SecureEquals(suppliedToken, configuredToken)) continue;

            var userId = session["UserId"]?.Trim() ?? "";
            var stationId = session["StationId"]?.Trim() ?? "";
            var shiftId = session["ShiftId"]?.Trim() ?? "";
            var roles = session.GetSection("Roles").Get<string[]>() ?? Array.Empty<string>();
            if (userId.Length == 0 || stationId.Length == 0 || shiftId.Length == 0 || roles.Length == 0 ||
                roles.Any(string.IsNullOrWhiteSpace))
                return Task.FromResult(AuthenticateResult.Fail("工位会话配置无效。"));

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(StationClaimTypes.StationId, stationId),
                new(StationClaimTypes.ShiftId, shiftId)
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }

        return Task.FromResult(AuthenticateResult.Fail("Bearer 令牌无效。"));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteErrorAsync(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "需要有效的工位会话。");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteErrorAsync(StatusCodes.Status403Forbidden, "FORBIDDEN", "当前角色无权执行此操作。");

    private Task WriteErrorAsync(int statusCode, string code, string message)
    {
        Response.StatusCode = statusCode;
        return Response.WriteAsJsonAsync(new ApiError(code, message, Context.TraceIdentifier));
    }

    private static bool SecureEquals(string supplied, string configured)
    {
        if (supplied.Length == 0 || configured.Length == 0) return false;
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(configured);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

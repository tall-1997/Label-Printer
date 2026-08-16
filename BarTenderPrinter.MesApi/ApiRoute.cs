namespace BarTenderPrinter.MesApi;

internal static class ApiRoute
{
    public static string Location(HttpContext context, string resourcePath)
    {
        var prefix = context.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? "/api/v1"
            : "/api";
        return $"{prefix}{resourcePath}";
    }
}

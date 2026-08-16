using BarTenderPrinter.Application.Idempotency;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.MesApi;

internal static class RequestIdentity
{
    private static readonly IRequestDigest Digest = new CanonicalRequestDigest();

    public static IdempotencyKey Key(HttpContext context, string? bodyKey)
    {
        var headerValues = context.Request.Headers["Idempotency-Key"];
        if (headerValues.Count > 1) throw new ArgumentException("Idempotency-Key 请求头只能出现一次。");
        var headerKey = headerValues.ToString().Trim();
        bodyKey = bodyKey?.Trim() ?? "";
        if (headerKey.Length == 0) return new IdempotencyKey(ApiValidation.Required(bodyKey, "idempotencyKey"));
        if (bodyKey.Length > 0 && !string.Equals(headerKey, bodyKey, StringComparison.Ordinal))
            throw new ArgumentException("正文 idempotencyKey 必须与 Idempotency-Key 请求头一致。", nameof(bodyKey));
        return new IdempotencyKey(headerKey);
    }

    public static string Hash<T>(T value) => Digest.Compute(value);
}

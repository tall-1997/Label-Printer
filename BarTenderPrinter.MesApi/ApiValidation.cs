using System.Text.RegularExpressions;

namespace BarTenderPrinter.MesApi;

public static class ApiValidation
{
    public static string Required(string? value, string fieldName, int maxLength = 128)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new ArgumentException($"{fieldName}长度必须为 1 到 {maxLength} 个字符。", fieldName);
        return normalized;
    }

    public static string Optional(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{fieldName}长度不能超过 {maxLength} 个字符。", fieldName);
        return normalized;
    }

    public static string RegexPattern(string? value)
    {
        var pattern = Optional(value, "validationPattern", 256);
        if (pattern.Length == 0) return pattern;
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("validationPattern必须是有效的正则表达式。", nameof(value));
        }
        return pattern;
    }
}

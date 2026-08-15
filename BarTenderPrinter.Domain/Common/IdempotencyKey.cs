namespace BarTenderPrinter.Domain.Common;

public readonly record struct IdempotencyKey
{
    public const int MaxLength = 128;

    public string Value { get; }

    public IdempotencyKey(string value)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > MaxLength)
            throw new ArgumentException($"幂等键长度必须为 1 到 {MaxLength} 个字符。", nameof(value));

        Value = normalized;
    }

    public override string ToString() => Value;
}

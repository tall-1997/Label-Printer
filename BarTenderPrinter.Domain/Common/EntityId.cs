namespace BarTenderPrinter.Domain.Common;

public readonly record struct EntityId
{
    public string Value { get; }

    public EntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("实体 ID 不能为空。", nameof(value));

        Value = value.Trim();
    }

    public static EntityId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

using System.Globalization;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Numbering;

public enum NumberType
{
    Imei,
    SerialNumber,
    Psn,
    Msn,
    Carton,
    Pallet
}

public enum NumberDatePattern
{
    None,
    YyMm,
    YyMmDd,
    YyyyMm,
    YyyyMmDd,
    YyDayOfYear,
    YyyyDayOfYear,
    MmDd
}

public enum NumberAllocationStatus
{
    Reserved,
    Assigned,
    Released,
    Scrapped,
    Frozen
}

public sealed record NumberAllocation(
    EntityId Id,
    EntityId RangeId,
    string Value,
    IdempotencyKey IdempotencyKey,
    string StationId,
    string OperatorId,
    DateTimeOffset AllocatedAtUtc)
{
    public NumberAllocationStatus Status { get; private set; } = NumberAllocationStatus.Reserved;
    public string UnitId { get; private set; } = "";

    public void Assign(string unitId)
    {
        if (Status != NumberAllocationStatus.Reserved)
            throw new InvalidOperationException("只有已保留号码可以分配给生产单元。");
        UnitId = Required(unitId, "生产单元 ID");
        Status = NumberAllocationStatus.Assigned;
    }

    public void Release()
    {
        if (!NumberAllocationPolicy.CanTransition(Status, NumberAllocationStatus.Released))
            throw new InvalidOperationException("只有已保留号码可以释放。");
        Status = NumberAllocationStatus.Released;
    }

    public void Scrap()
    {
        if (!NumberAllocationPolicy.CanTransition(Status, NumberAllocationStatus.Scrapped))
            throw new InvalidOperationException("当前号码状态无法报废。");
        Status = NumberAllocationStatus.Scrapped;
    }

    public void Freeze()
    {
        if (!NumberAllocationPolicy.CanTransition(Status, NumberAllocationStatus.Frozen))
            throw new InvalidOperationException("当前号码状态无法冻结。");
        Status = NumberAllocationStatus.Frozen;
    }

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{name}不能为空。", nameof(value));
        return normalized;
    }
}

public sealed class NumberRange
{
    private readonly object _sync = new();
    private readonly Dictionary<string, NumberAllocation> _allocationsByKey = new(StringComparer.Ordinal);
    private bool _isExhausted;

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public NumberType Type { get; }
    public string Prefix { get; }
    public NumberDatePattern DatePattern { get; }
    public long Start { get; }
    public long End { get; }
    public long Step { get; }
    public int NumericWidth { get; }
    public string ValidationPattern { get; }
    public long NextValue { get; private set; }
    public long Version { get; private set; }

    public NumberRange(
        EntityId id,
        EntityId orderId,
        NumberType type,
        string prefix,
        NumberDatePattern datePattern,
        long start,
        long end,
        long step = 1,
        int numericWidth = 0,
        string validationPattern = "")
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "号段起始值必须大于或等于零。");
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end), "号段结束值必须大于或等于起始值。");
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "号段步长必须大于零。");
        if (numericWidth < 0 || numericWidth > 32) throw new ArgumentOutOfRangeException(nameof(numericWidth));
        if (numericWidth > 0 && start.ToString(CultureInfo.InvariantCulture).Length > numericWidth)
            throw new ArgumentException("号段起始值超过数字宽度。", nameof(numericWidth));
        if (numericWidth > 0 && end.ToString(CultureInfo.InvariantCulture).Length > numericWidth)
            throw new ArgumentException("号段结束值超过数字宽度。", nameof(numericWidth));

        Id = id;
        OrderId = orderId;
        Type = type;
        Prefix = prefix?.Trim() ?? "";
        DatePattern = datePattern;
        Start = start;
        End = end;
        Step = step;
        NumericWidth = numericWidth;
        ValidationPattern = validationPattern?.Trim() ?? "";
        NextValue = start;
    }

    public OperationResult<NumberAllocation> Allocate(
        IdempotencyKey idempotencyKey,
        string stationId,
        string operatorId,
        DateTimeOffset utcNow)
    {
        if (utcNow.Offset != TimeSpan.Zero) throw new ArgumentException("分配时间必须使用 UTC。", nameof(utcNow));
        stationId = Required(stationId, "工位 ID");
        operatorId = Required(operatorId, "操作员 ID");

        lock (_sync)
        {
            if (_allocationsByKey.TryGetValue(idempotencyKey.Value, out var existing))
                return OperationResult<NumberAllocation>.Success(existing);
            if (_isExhausted)
                return OperationResult<NumberAllocation>.Failure("NUMBER_RANGE_EXHAUSTED", "号段已耗尽。");

            var allocation = new NumberAllocation(
                EntityId.New(),
                Id,
                Format(NextValue, utcNow),
                idempotencyKey,
                stationId,
                operatorId,
                utcNow);
            _allocationsByKey.Add(idempotencyKey.Value, allocation);
            if (End - NextValue < Step)
            {
                _isExhausted = true;
            }
            else
            {
                NextValue += Step;
            }
            Version++;
            return OperationResult<NumberAllocation>.Success(allocation);
        }
    }

    public NumberAllocation? FindByIdempotencyKey(IdempotencyKey key)
    {
        lock (_sync)
            return _allocationsByKey.GetValueOrDefault(key.Value);
    }

    public string Format(long value, DateTimeOffset utcNow)
    {
        if (value < Start || value > End) throw new ArgumentOutOfRangeException(nameof(value));
        if (utcNow.Offset != TimeSpan.Zero) throw new ArgumentException("格式化时间必须使用 UTC。", nameof(utcNow));
        var number = NumericWidth == 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString($"D{NumericWidth}", CultureInfo.InvariantCulture);
        return Prefix + FormatDate(utcNow, DatePattern) + number;
    }

    private static string FormatDate(DateTimeOffset value, NumberDatePattern pattern) => pattern switch
    {
        NumberDatePattern.None => "",
        NumberDatePattern.YyMm => value.ToString("yyMM", CultureInfo.InvariantCulture),
        NumberDatePattern.YyMmDd => value.ToString("yyMMdd", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyMm => value.ToString("yyyyMM", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyMmDd => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        NumberDatePattern.YyDayOfYear => value.ToString("yy", CultureInfo.InvariantCulture) + value.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyDayOfYear => value.ToString("yyyy", CultureInfo.InvariantCulture) + value.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
        NumberDatePattern.MmDd => value.ToString("MMdd", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{name}不能为空。", nameof(value));
        return normalized;
    }
}

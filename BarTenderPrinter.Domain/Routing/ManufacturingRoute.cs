using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Routing;

public enum RouteType
{
    Standard,
    Rework
}

public sealed record ManufacturingOperation
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Sequence { get; init; }
}

public sealed class ManufacturingRoute
{
    private readonly IReadOnlyList<ManufacturingOperation> _operations;

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public string Name { get; }
    public RouteType Type { get; }
    public IReadOnlyList<ManufacturingOperation> Operations => _operations;

    public ManufacturingRoute(
        EntityId id,
        EntityId orderId,
        string name,
        RouteType type,
        IEnumerable<ManufacturingOperation> operations)
    {
        Id = id;
        OrderId = orderId;
        Name = Required(name, "路线名称");
        var normalized = (operations ?? throw new ArgumentNullException(nameof(operations)))
            .Select(operation => new ManufacturingOperation
            {
                Id = Required(operation.Id, "工序 ID"),
                Name = Required(operation.Name, "工序名称"),
                Sequence = operation.Sequence
            })
            .OrderBy(operation => operation.Sequence)
            .ToArray();
        if (normalized.Length == 0) throw new ArgumentException("工艺路线至少包含一个工序。", nameof(operations));
        if (normalized.Any(operation => operation.Sequence <= 0))
            throw new ArgumentException("工序序号必须大于零。", nameof(operations));
        if (normalized.Select(operation => operation.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new ArgumentException("工艺路线中的工序 ID 必须唯一。", nameof(operations));
        if (normalized.Select(operation => operation.Sequence).Distinct().Count() != normalized.Length)
            throw new ArgumentException("工艺路线中的工序序号必须唯一。", nameof(operations));

        Type = type;
        _operations = normalized;
    }

    public ManufacturingOperation? FindOperation(string operationId) =>
        _operations.FirstOrDefault(operation =>
            string.Equals(operation.Id, operationId?.Trim(), StringComparison.OrdinalIgnoreCase));

    public ManufacturingOperation FirstOperation => _operations[0];

    public ManufacturingOperation? GetPrevious(string operationId)
    {
        var index = Array.FindIndex(_operations.ToArray(), operation =>
            string.Equals(operation.Id, operationId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return index > 0 ? _operations[index - 1] : null;
    }

    public ManufacturingOperation? GetNext(string operationId)
    {
        var index = Array.FindIndex(_operations.ToArray(), operation =>
            string.Equals(operation.Id, operationId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < _operations.Count - 1 ? _operations[index + 1] : null;
    }

    private static string Required(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }
}

public sealed class Station
{
    private readonly HashSet<string> _qualifiedOperationIds;

    public EntityId Id { get; }
    public string Name { get; }
    public IReadOnlySet<string> QualifiedOperationIds => _qualifiedOperationIds;

    public Station(EntityId id, string name, IEnumerable<string> qualifiedOperationIds)
    {
        Id = id;
        Name = Required(name, "工位名称");
        _qualifiedOperationIds = new HashSet<string>(
            (qualifiedOperationIds ?? throw new ArgumentNullException(nameof(qualifiedOperationIds)))
                .Select(operationId => Required(operationId, "工序 ID")),
            StringComparer.OrdinalIgnoreCase);
        if (_qualifiedOperationIds.Count == 0)
            throw new ArgumentException("工位至少需要一个工序资格。", nameof(qualifiedOperationIds));
    }

    public bool IsQualifiedFor(string operationId) =>
        _qualifiedOperationIds.Contains(operationId?.Trim() ?? "");

    private static string Required(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }
}

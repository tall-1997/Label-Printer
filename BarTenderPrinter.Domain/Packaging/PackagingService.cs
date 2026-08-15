using System.Collections.ObjectModel;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Packaging;

public static class PackagingErrorCodes
{
    public const string UnitNotFound = "PACKAGING_UNIT_NOT_FOUND";
    public const string BindingConflict = "PACKAGING_BINDING_CONFLICT";
    public const string TypeMismatch = "PACKAGING_TYPE_MISMATCH";
    public const string ProductMismatch = "PACKAGING_PRODUCT_MISMATCH";
    public const string CapacityExceeded = "PACKAGING_CAPACITY_EXCEEDED";
    public const string UnitClosed = "PACKAGING_UNIT_CLOSED";
    public const string ChildNotReady = "PACKAGING_CHILD_NOT_READY";
    public const string ConcurrencyConflict = "PACKAGING_CONCURRENCY_CONFLICT";
}

public sealed record PackagingOperationResult
{
    public bool IsSuccess { get; init; }
    public PackagingBinding? Binding { get; init; }
    public PackagingPrintIntent? PrintIntent { get; init; }
    public OperationError? Error { get; init; }

    public static PackagingOperationResult Success(
        PackagingBinding? binding = null,
        PackagingPrintIntent? printIntent = null) => new()
    {
        IsSuccess = true,
        Binding = binding,
        PrintIntent = printIntent
    };

    public static PackagingOperationResult Failure(string code, string message) => new()
    {
        Error = new OperationError(code, message)
    };
}

public sealed class PackagingService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PackagingUnit> _units = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackagingBinding> _activeBindingByChild = new(StringComparer.Ordinal);
    private readonly List<PackagingBinding> _bindings = new();
    private readonly Dictionary<string, PackagingPrintIntent> _printIntentsByUnit = new(StringComparer.Ordinal);

    public void Register(PackagingUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        lock (_sync)
        {
            if (!_units.TryAdd(unit.Id.Value, unit))
                throw new InvalidOperationException("包装单元已经注册。");
            if (_units.Values.Any(existing =>
                    existing.Id != unit.Id &&
                    string.Equals(existing.Code, unit.Code, StringComparison.OrdinalIgnoreCase)))
            {
                _units.Remove(unit.Id.Value);
                throw new InvalidOperationException("包装码必须唯一。");
            }
        }
    }

    public PackagingUnit? Get(EntityId id)
    {
        lock (_sync)
            return _units.GetValueOrDefault(id.Value);
    }

    public PackagingBinding? GetActiveBinding(EntityId childId)
    {
        lock (_sync)
            return _activeBindingByChild.GetValueOrDefault(childId.Value);
    }

    public IReadOnlyList<PackagingBinding> GetBindings(EntityId parentId)
    {
        lock (_sync)
            return _bindings.Where(binding => binding.ParentId == parentId).ToArray();
    }

    public PackagingOperationResult Bind(
        EntityId parentId,
        EntityId childId,
        long expectedParentVersion,
        long expectedChildVersion,
        string operatorId,
        DateTimeOffset utcNow)
    {
        ValidateUtc(utcNow);
        operatorId = Required(operatorId, "操作员 ID");

        lock (_sync)
        {
            if (!_units.TryGetValue(parentId.Value, out var parent) || !_units.TryGetValue(childId.Value, out var child))
                return PackagingOperationResult.Failure(PackagingErrorCodes.UnitNotFound, "父级或子级包装单元不存在。");
            if (parent.Version != expectedParentVersion || child.Version != expectedChildVersion)
                return PackagingOperationResult.Failure(PackagingErrorCodes.ConcurrencyConflict, "包装单元版本已变化，请刷新后重试。");
            if (parent.Id == child.Id || !IsAllowedRelationship(parent.Type, child.Type))
                return PackagingOperationResult.Failure(PackagingErrorCodes.TypeMismatch, "包装层级关系无效。");
            if (parent.OrderId != child.OrderId ||
                !string.Equals(parent.ProductModel, child.ProductModel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parent.Color, child.Color, StringComparison.OrdinalIgnoreCase))
            {
                return PackagingOperationResult.Failure(PackagingErrorCodes.ProductMismatch, "订单、型号或颜色不一致。");
            }
            if (parent.Status != PackagingUnitStatus.Open)
                return PackagingOperationResult.Failure(PackagingErrorCodes.UnitClosed, "父级包装单元已经关闭。");
            if (child.Status != PackagingUnitStatus.Closed)
                return PackagingOperationResult.Failure(PackagingErrorCodes.ChildNotReady, "子级包装单元尚未关闭。");
            if (_activeBindingByChild.ContainsKey(child.Id.Value))
                return PackagingOperationResult.Failure(PackagingErrorCodes.BindingConflict, "子级包装单元已绑定到其他活动父级。");
            if (parent.ChildIds.Count >= parent.Capacity)
                return PackagingOperationResult.Failure(PackagingErrorCodes.CapacityExceeded, "父级包装单元已达到容量上限。");

            parent.AddChild(child.Id);
            var binding = new PackagingBinding(parent.Id, child.Id, utcNow, operatorId);
            _activeBindingByChild.Add(child.Id.Value, binding);
            _bindings.Add(binding);

            PackagingPrintIntent? printIntent = null;
            if (parent.IsFull)
            {
                parent.Close();
                if (parent.Type is PackagingUnitType.Carton or PackagingUnitType.Pallet)
                    printIntent = CreatePrintIntent(parent, utcNow);
            }

            return PackagingOperationResult.Success(binding, printIntent);
        }
    }

    public PackagingOperationResult Unbind(
        EntityId parentId,
        EntityId childId,
        long expectedParentVersion,
        string operatorId,
        DateTimeOffset utcNow)
    {
        ValidateUtc(utcNow);
        _ = Required(operatorId, "操作员 ID");

        lock (_sync)
        {
            if (!_units.TryGetValue(parentId.Value, out var parent) || !_units.ContainsKey(childId.Value))
                return PackagingOperationResult.Failure(PackagingErrorCodes.UnitNotFound, "父级或子级包装单元不存在。");
            if (parent.Version != expectedParentVersion)
                return PackagingOperationResult.Failure(PackagingErrorCodes.ConcurrencyConflict, "包装单元版本已变化，请刷新后重试。");
            if (parent.Status != PackagingUnitStatus.Open)
                return PackagingOperationResult.Failure(PackagingErrorCodes.UnitClosed, "已关闭包装单元需要通过返工流程处理。");
            if (!_activeBindingByChild.TryGetValue(childId.Value, out var binding) || binding.ParentId != parentId)
                return PackagingOperationResult.Failure(PackagingErrorCodes.BindingConflict, "活动绑定关系不存在。");

            parent.RemoveChild(childId);
            _activeBindingByChild.Remove(childId.Value);
            _bindings.Remove(binding);
            return PackagingOperationResult.Success(binding);
        }
    }

    private PackagingPrintIntent CreatePrintIntent(PackagingUnit unit, DateTimeOffset utcNow)
    {
        if (_printIntentsByUnit.TryGetValue(unit.Id.Value, out var existing)) return existing;
        var childCodes = unit.ChildIds.Select(id => _units[id.Value].Code).ToArray();
        var fields = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PACKAGE_CODE"] = unit.Code,
            ["ORDER_ID"] = unit.OrderId.Value,
            ["PRODUCT_MODEL"] = unit.ProductModel,
            ["COLOR"] = unit.Color,
            ["QUANTITY"] = childCodes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CHILD_CODES"] = string.Join(",", childCodes)
        });
        var intent = new PackagingPrintIntent
        {
            Id = EntityId.New(),
            PackagingUnitId = unit.Id,
            LabelType = unit.Type == PackagingUnitType.Carton ? LabelType.Carton : LabelType.Pallet,
            Fields = fields,
            CreatedAtUtc = utcNow
        };
        _printIntentsByUnit.Add(unit.Id.Value, intent);
        return intent;
    }

    private static bool IsAllowedRelationship(PackagingUnitType parent, PackagingUnitType child) =>
        (parent, child) is
            (PackagingUnitType.ColorBox, PackagingUnitType.Body) or
            (PackagingUnitType.Carton, PackagingUnitType.ColorBox) or
            (PackagingUnitType.Pallet, PackagingUnitType.Carton);

    private static string Required(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }

    private static void ValidateUtc(DateTimeOffset utcNow)
    {
        if (utcNow.Offset != TimeSpan.Zero) throw new ArgumentException("包装操作时间必须使用 UTC。", nameof(utcNow));
    }
}

using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Rework;

namespace BarTenderPrinter.Domain.Routing;

public static class StationPassErrorCodes
{
    public const string OrderUnavailable = "ORDER_UNAVAILABLE";
    public const string StationNotQualified = "STATION_NOT_QUALIFIED";
    public const string UnitUnavailable = "UNIT_UNAVAILABLE";
    public const string RouteMismatch = "ROUTE_MISMATCH";
    public const string OperationNotFound = "OPERATION_NOT_FOUND";
    public const string PreviousOperationIncomplete = "PREVIOUS_OPERATION_INCOMPLETE";
    public const string OperationAlreadyCompleted = "OPERATION_ALREADY_COMPLETED";
    public const string ReworkContextRequired = "REWORK_CONTEXT_REQUIRED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
}

public sealed record StationPassCommand
{
    public required ProductionOrder Order { get; init; }
    public required ProductionUnit Unit { get; init; }
    public required ManufacturingRoute Route { get; init; }
    public required Station Station { get; init; }
    public required string OperationId { get; init; }
    public required string OperatorId { get; init; }
    public required IdempotencyKey IdempotencyKey { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public ReworkOrder? ReworkOrder { get; init; }
}

public sealed record StationPassRecord(
    EntityId Id,
    EntityId UnitId,
    EntityId OrderId,
    EntityId RouteId,
    string OperationId,
    EntityId StationId,
    string OperatorId,
    DateTimeOffset OccurredAtUtc,
    IdempotencyKey IdempotencyKey,
    string ReworkOrderId,
    int ReworkSequence);

public sealed record StationPassResult
{
    public bool IsSuccess { get; init; }
    public StationPassRecord? Record { get; init; }
    public OperationError? Error { get; init; }
    public string MissingOperationId { get; init; } = "";

    public static StationPassResult Success(StationPassRecord record) => new()
    {
        IsSuccess = true,
        Record = record
    };

    public static StationPassResult Failure(string code, string message, string missingOperationId = "") => new()
    {
        Error = new OperationError(code, message),
        MissingOperationId = missingOperationId
    };
}

public interface IStationPassService
{
    StationPassResult Pass(StationPassCommand command);
    IReadOnlyList<StationPassRecord> GetRouteHistory(EntityId unitId);
}

public sealed class StationPassService : IStationPassService
{
    private sealed record StoredResult(string RequestSignature, StationPassResult Result);

    private readonly object _sync = new();
    private readonly Dictionary<string, StoredResult> _resultsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StationPassRecord>> _historyByUnit = new(StringComparer.Ordinal);

    public StationPassResult Pass(StationPassCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        var signature = CreateSignature(command);

        lock (_sync)
        {
            if (_resultsByKey.TryGetValue(command.IdempotencyKey.Value, out var stored))
            {
                return stored.RequestSignature == signature
                    ? stored.Result
                    : StationPassResult.Failure(StationPassErrorCodes.IdempotencyConflict, "幂等键已用于其他过站请求。");
            }

            var result = EvaluateAndPass(command);
            _resultsByKey.Add(command.IdempotencyKey.Value, new StoredResult(signature, result));
            return result;
        }
    }

    public IReadOnlyList<StationPassRecord> GetRouteHistory(EntityId unitId)
    {
        lock (_sync)
            return _historyByUnit.TryGetValue(unitId.Value, out var records)
                ? records.ToArray()
                : Array.Empty<StationPassRecord>();
    }

    private StationPassResult EvaluateAndPass(StationPassCommand command)
    {
        if (!command.Order.AcceptsStationPass(command.OccurredAtUtc))
            return StationPassResult.Failure(StationPassErrorCodes.OrderUnavailable, "订单当前不接受过站。");
        if (command.Route.OrderId != command.Order.Id || command.Unit.OrderId != command.Order.Id)
            return StationPassResult.Failure(StationPassErrorCodes.RouteMismatch, "订单、生产单元和工艺路线不匹配。");
        if (command.Unit.Status != ProductionUnitStatus.Active)
            return StationPassResult.Failure(StationPassErrorCodes.UnitUnavailable, "生产单元当前不接受过站。");

        var operation = command.Route.FindOperation(command.OperationId);
        if (operation == null)
            return StationPassResult.Failure(StationPassErrorCodes.OperationNotFound, "工艺路线中不存在指定工序。");
        if (!command.Station.IsQualifiedFor(operation.Id))
            return StationPassResult.Failure(StationPassErrorCodes.StationNotQualified, "当前工位未取得指定工序资格。");

        var reworkValidation = ValidateRework(command);
        if (reworkValidation != null) return reworkValidation;

        var history = GetMutableHistory(command.Unit.Id);
        var routeHistory = history.Where(record =>
            record.RouteId == command.Route.Id &&
            string.Equals(record.ReworkOrderId, command.ReworkOrder?.Id.Value ?? "", StringComparison.Ordinal)).ToArray();
        if (routeHistory.Any(record => string.Equals(record.OperationId, operation.Id, StringComparison.OrdinalIgnoreCase)))
            return StationPassResult.Failure(StationPassErrorCodes.OperationAlreadyCompleted, "指定工序已经完成。");

        var previous = command.Route.GetPrevious(operation.Id);
        if (previous != null && !routeHistory.Any(record =>
                string.Equals(record.OperationId, previous.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return StationPassResult.Failure(
                StationPassErrorCodes.PreviousOperationIncomplete,
                "上一工序尚未完成。",
                previous.Id);
        }

        command.Unit.MoveToOperation(operation.Id);
        var record = new StationPassRecord(
            EntityId.New(),
            command.Unit.Id,
            command.Order.Id,
            command.Route.Id,
            operation.Id,
            command.Station.Id,
            command.OperatorId.Trim(),
            command.OccurredAtUtc,
            command.IdempotencyKey,
            command.ReworkOrder?.Id.Value ?? "",
            command.ReworkOrder?.Sequence ?? 0);
        history.Add(record);
        return StationPassResult.Success(record);
    }

    private static StationPassResult? ValidateRework(StationPassCommand command)
    {
        if (command.Route.Type == RouteType.Standard && command.ReworkOrder == null) return null;
        if (command.Route.Type == RouteType.Standard || command.ReworkOrder == null)
            return StationPassResult.Failure(StationPassErrorCodes.ReworkContextRequired, "返工路线和返工任务必须同时提供。");
        if (command.ReworkOrder.Status != ReworkOrderStatus.Active ||
            command.ReworkOrder.ProductionUnitId != command.Unit.Id ||
            command.ReworkOrder.RouteId != command.Route.Id)
        {
            return StationPassResult.Failure(StationPassErrorCodes.ReworkContextRequired, "返工任务与生产单元或路线不匹配。");
        }

        return null;
    }

    private List<StationPassRecord> GetMutableHistory(EntityId unitId)
    {
        if (!_historyByUnit.TryGetValue(unitId.Value, out var history))
        {
            history = new List<StationPassRecord>();
            _historyByUnit.Add(unitId.Value, history);
        }
        return history;
    }

    private static void ValidateCommand(StationPassCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Order);
        ArgumentNullException.ThrowIfNull(command.Unit);
        ArgumentNullException.ThrowIfNull(command.Route);
        ArgumentNullException.ThrowIfNull(command.Station);
        if (string.IsNullOrWhiteSpace(command.OperationId)) throw new ArgumentException("工序 ID 不能为空。", nameof(command));
        if (string.IsNullOrWhiteSpace(command.OperatorId)) throw new ArgumentException("操作员 ID 不能为空。", nameof(command));
        if (command.OccurredAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("过站时间必须使用 UTC。", nameof(command));
    }

    private static string CreateSignature(StationPassCommand command) => string.Join("|",
        command.Order.Id.Value,
        command.Unit.Id.Value,
        command.Route.Id.Value,
        command.OperationId.Trim().ToUpperInvariant(),
        command.Station.Id.Value,
        command.OperatorId.Trim(),
        command.ReworkOrder?.Id.Value ?? "");
}

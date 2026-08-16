using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
using BarTenderPrinter.Persistence;

namespace BarTenderPrinter.MesApi;

public static class CoreEndpoints
{
    public static RouteGroupBuilder MapCoreEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/orders/{id}/transitions", TransitionOrder).RequireAuthorization("Planner");
        api.MapPost("/production-units", CreateProductionUnit).RequireAuthorization("ProductionMasterData");
        api.MapPost("/routes", CreateRoute).RequireAuthorization("ProcessEngineer");
        api.MapPost("/stations", CreateStation).RequireAuthorization("ProcessEngineer");
        api.MapPost("/packaging-units", CreatePackagingUnit).RequireAuthorization("ProductionMasterData");
        api.MapPost("/number-allocations/{id}/status", ChangeNumberStatus).RequireAuthorization("NumberDisposition");
        api.MapGet("/number-allocations/{id}/history", GetNumberHistory).RequireAuthorization("NumberDisposition");
        api.MapPost("/weight-rules", CreateWeightRule).RequireAuthorization("ProcessEngineer");
        api.MapPost("/packaging-units/{id}/weights", RecordWeight).RequireAuthorization("StationOperator");
        api.MapPost("/identifier-write-tasks", CreateWriteTask).RequireAuthorization("StationOperator");
        api.MapPost("/identifier-write-tasks/claims", ClaimWriteTask).RequireAuthorization("StationOperator");
        api.MapPost("/identifier-write-tasks/{id}/results", RecordWriteResult).RequireAuthorization("StationOperator");
        return api;
    }

    private static async Task<IResult> TransitionOrder(string id, TransitionOrderRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        if (request.ExpectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion));
        var result = await repository.TransitionOrderAsync(id, request.TargetStatus, request.ExpectedVersion,
            Key(context, request.IdempotencyKey), Hash(new { id, request.TargetStatus, request.ExpectedVersion }),
            DateTimeOffset.UtcNow, cancellationToken,
            (before, after) => AuditSnapshot.Create(context, "OrderStatusChanged", "ProductionOrder", id, before, after));
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateProductionUnit(CreateProductionUnitRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var orderId = ApiValidation.Required(request.OrderId, "orderId");
        var allocations = request.AllocationIds?.ToDictionary(x => x.Key,
            x => ApiValidation.Required(x.Value, "allocationId"))
            ?? throw new ArgumentException("allocationIds不能为空。", nameof(request.AllocationIds));
        var result = await repository.CreateProductionUnitAsync(orderId, allocations,
            Key(context, request.IdempotencyKey), Hash(new { orderId, allocations }), DateTimeOffset.UtcNow,
            cancellationToken, value => AuditSnapshot.Create(context, "ProductionUnitCreated", "ProductionUnit",
                value.Id, null, value));
        return Results.Created($"/api/production-units/{result.Id}", result);
    }

    private static async Task<IResult> CreateRoute(CreateRouteRequest request, MesCoreRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var operations = request.Operations?.Select(x => new ManufacturingOperation
        {
            Id = ApiValidation.Required(x.Id, "operationId"),
            Name = ApiValidation.Required(x.Name, "operationName"),
            Sequence = x.Sequence
        }).ToArray() ?? throw new ArgumentException("operations不能为空。", nameof(request.Operations));
        var route = new ManufacturingRoute(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
            ApiValidation.Required(request.Name, "name"), request.RouteType, operations);
        var result = await repository.CreateRouteAsync(route, Key(context, request.IdempotencyKey),
            Hash(new { route.OrderId, route.Name, route.Type, operations }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "RouteCreated", "ManufacturingRoute", value.Id, null, value));
        return Results.Created($"/api/routes/{result.Id}", result);
    }

    private static async Task<IResult> CreateStation(CreateStationRequest request, MesCoreRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var station = new Station(EntityId.New(), ApiValidation.Required(request.Name, "name"),
            request.QualifiedOperationIds ?? throw new ArgumentException("qualifiedOperationIds不能为空。"));
        var result = await repository.CreateStationAsync(station, Key(context, request.IdempotencyKey),
            Hash(new { station.Name, station.QualifiedOperationIds }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "StationCreated", "Station", value.Id, null, value));
        return Results.Created($"/api/stations/{result.Id}", result);
    }

    private static async Task<IResult> CreatePackagingUnit(CreatePackagingUnitRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var productionUnitId = string.IsNullOrWhiteSpace(request.ProductionUnitId)
            ? (EntityId?)null
            : new EntityId(ApiValidation.Required(request.ProductionUnitId, "productionUnitId"));
        var unit = new PackagingUnit(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
            request.UnitType, ApiValidation.Required(request.Code, "code"),
            ApiValidation.Required(request.ProductModel, "productModel"), ApiValidation.Required(request.Color, "color"),
            request.Capacity, productionUnitId);
        var result = await repository.CreatePackagingUnitAsync(unit, Key(context, request.IdempotencyKey),
            Hash(new { unit.OrderId, unit.Type, unit.Code, unit.ProductModel, unit.Color, unit.Capacity, unit.ProductionUnitId }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "PackagingUnitCreated", "PackagingUnit", value.Id, null, value));
        return Results.Created($"/api/packaging-units/{result.Id}", result);
    }

    private static async Task<IResult> ChangeNumberStatus(string id, ChangeNumberStatusRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var session = Session(context);
        var reason = ApiValidation.Required(request.ReasonCode, "reasonCode");
        var result = await repository.ChangeNumberStatusAsync(ApiValidation.Required(id, "id"), request.TargetStatus,
            reason, session.UserId, session.StationId, Key(context, request.IdempotencyKey),
            Hash(new { id, request.TargetStatus, reason, session.UserId, session.StationId }), DateTimeOffset.UtcNow,
            cancellationToken, value => AuditSnapshot.Create(context, "NumberStatusChanged", "NumberAllocation", id,
                null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetNumberHistory(string id, MesCoreRepository repository,
        CancellationToken cancellationToken) => Results.Ok(await repository.GetNumberHistoryAsync(
            ApiValidation.Required(id, "id"), cancellationToken));

    private static async Task<IResult> CreateWeightRule(CreateWeightRuleRequest request, MesCoreRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var rule = new WeightRule(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
            request.PackagingUnitType.ToString(), request.MinimumWeight, request.MaximumWeight,
            ApiValidation.Required(request.Unit, "unit"));
        var result = await repository.CreateWeightRuleAsync(rule, Key(context, request.IdempotencyKey),
            Hash(new { rule.OrderId, rule.PackagingUnitType, rule.MinimumWeight, rule.MaximumWeight, rule.Unit }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "WeightRuleCreated", "WeightRule", value.Id, null, value));
        return Results.Created($"/api/weight-rules/{result.Id}", result);
    }

    private static async Task<IResult> RecordWeight(string id, RecordWeightRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await repository.RecordWeightAsync(ApiValidation.Required(id, "id"), request.Weight,
            ApiValidation.Required(request.Unit, "unit"), ApiValidation.Required(request.DeviceId, "deviceId"),
            request.IsSimulated, Key(context, request.IdempotencyKey),
            Hash(new { id, request.Weight, request.Unit, request.DeviceId, request.IsSimulated }), DateTimeOffset.UtcNow,
            cancellationToken, value => AuditSnapshot.Create(context, "WeightMeasured", "PackagingUnit", id, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateWriteTask(CreateIdentifierWriteTaskRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var allocationIds = request.AllocationIds?.Select(x => ApiValidation.Required(x, "allocationId")).ToArray()
            ?? throw new ArgumentException("allocationIds不能为空。", nameof(request.AllocationIds));
        var platform = ApiValidation.Required(request.Platform, "platform");
        var targetStationId = ApiValidation.Required(request.TargetStationId, "targetStationId");
        var result = await repository.CreateWriteTaskAsync(ApiValidation.Required(request.UnitId, "unitId"), allocationIds,
            platform, targetStationId, Key(context, request.IdempotencyKey),
            Hash(new { request.UnitId, allocationIds, platform, targetStationId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "IdentifierWriteTaskCreated", "IdentifierWriteTask", value.Id, null, value));
        return Results.Created($"/api/identifier-write-tasks/{result.Id}", result);
    }

    private static async Task<IResult> ClaimWriteTask(ClaimIdentifierWriteTaskRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var session = Session(context);
        var platform = ApiValidation.Required(request.Platform, "platform");
        var result = await repository.ClaimWriteTaskAsync(session.StationId, session.UserId, platform,
            Key(context, request.IdempotencyKey), Hash(new { session.StationId, session.UserId, platform }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "IdentifierWriteTaskClaimed", "IdentifierWriteTask", value.Id,
                new { State = "Pending" }, value));
        return result.Task == null ? Results.NoContent() : Results.Ok(result);
    }

    private static async Task<IResult> RecordWriteResult(string id, IdentifierWriteResultRequest request,
        MesCoreRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        if (request.Result.ValueKind != JsonValueKind.Object) throw new ArgumentException("result必须为JSON对象。");
        var session = Session(context);
        var resultJson = request.Result.GetRawText();
        var diagnosticCode = ApiValidation.Optional(request.DiagnosticCode, "diagnosticCode", 256);
        var result = await repository.RecordWriteResultAsync(ApiValidation.Required(id, "id"), session.StationId,
            request.State, resultJson, diagnosticCode,
            Key(context, request.IdempotencyKey), Hash(new { id, session.StationId, request.State, resultJson, diagnosticCode }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "IdentifierWriteTaskCompleted", "IdentifierWriteTask", id,
                new { State = "InProgress" }, value));
        return Results.Ok(result);
    }

    private static StationSession Session(HttpContext context) =>
        context.Items[typeof(StationSession)] as StationSession ?? StationSessionAccessor.Get(context.User);
    private static IdempotencyKey Key(HttpContext context, string? bodyKey)
    {
        var headerKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        bodyKey = bodyKey?.Trim() ?? "";
        if (headerKey.Length == 0)
            return new IdempotencyKey(ApiValidation.Required(bodyKey, "idempotencyKey"));
        if (bodyKey.Length > 0 && !string.Equals(headerKey, bodyKey, StringComparison.Ordinal))
            throw new ArgumentException("正文 idempotencyKey 必须与 Idempotency-Key 请求头一致。", nameof(bodyKey));
        return new IdempotencyKey(headerKey);
    }
    private static string Hash<T>(T value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}

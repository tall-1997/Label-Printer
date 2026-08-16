using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Quality;
using BarTenderPrinter.Domain.Rework;
using BarTenderPrinter.Domain.Shipping;
using BarTenderPrinter.Persistence;
using Microsoft.AspNetCore.Http.Features;

namespace BarTenderPrinter.MesApi;

public static class ExtendedEndpoints
{
    private const long MaximumCsvBytes = 10 * 1024 * 1024;

    public static RouteGroupBuilder MapExtendedEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/inspection-lots", CreateInspectionLot).RequireAuthorization("QualityOperator");
        api.MapPost("/inspection-lots/{lotId}/results", AddInspectionResult).RequireAuthorization("QualityOperator");
        api.MapPost("/inspection-lots/{lotId}/complete", CompleteInspectionLot).RequireAuthorization("QualityOperator");
        api.MapPost("/inspection-lots/{lotId}/disposition", ApplyDisposition).RequireAuthorization("DispositionApprover");
        api.MapGet("/quality-disposition-tasks", GetDispositionTasks).RequireAuthorization("QualityOperator");
        api.MapPost("/rework-orders", CreateReworkOrder).RequireAuthorization("ReworkCreator");
        api.MapPost("/rework-orders/{id}/approve", ApproveReworkOrder).RequireAuthorization("ReworkApprover");
        api.MapPost("/rework-orders/{id}/activate", ActivateReworkOrder).RequireAuthorization("ReworkExecutor");
        api.MapPost("/rework-orders/{id}/complete", CompleteReworkOrder).RequireAuthorization("ReworkExecutor");
        api.MapPost("/shipments", CreateShipment).RequireAuthorization("WarehouseOperator");
        api.MapPost("/shipments/{id}/cartons", AddShipmentCarton).RequireAuthorization("WarehouseOperator");
        api.MapPost("/shipments/{id}/confirm", ConfirmShipment).RequireAuthorization("ShipmentConfirmer");
        api.MapPost("/orders/{orderId}/archive", ArchiveOrder).RequireAuthorization("ArchiveOperator");
        api.MapGet("/orders/{orderId}/archive", GetArchive);
        api.MapGet("/archive-repair-tasks", GetArchiveRepairTasks).RequireAuthorization("ArchiveOperator");
        api.MapPost("/archive-repair-tasks/{id}/repair", RepairArchive).RequireAuthorization("ArchiveOperator");
        api.MapPost("/csv-imports/{type}", StageCsvImport).RequireAuthorization("DataImporter");
        api.MapGet("/csv-imports/{id}", GetCsvImport).RequireAuthorization("DataImporter");
        api.MapPost("/csv-imports/{id}/confirm", ConfirmCsvImport).RequireAuthorization("DataImporter");
        api.MapGet("/csv-exports/orders", ExportOrders).RequireAuthorization("DataExporter");
        api.MapGet("/csv-exports/number-ranges", ExportNumberRanges).RequireAuthorization("DataExporter");
        api.MapGet("/csv-exports/traceability", ExportTraceability).RequireAuthorization("DataExporter");
        return api;
    }

    private static async Task<IResult> CreateInspectionLot(CreateInspectionLotRequest request,
        InspectionRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var sampleIds = request.SampleUnitIds?.Select(id => new EntityId(ApiValidation.Required(id, "sampleUnitId"))).ToArray()
            ?? throw new ArgumentException("sampleUnitIds不能为空。", nameof(request.SampleUnitIds));
        var lot = new InspectionLot(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
            ApiValidation.Required(request.InspectionType, "inspectionType", 64),
            ApiValidation.Required(request.SampleRule, "sampleRule", 256), sampleIds);
        var key = Key(context, request.IdempotencyKey);
        var result = await repository.CreateLotAsync(lot, DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "InspectionLotCreated", "InspectionLot", value.Id, null, value),
            key, Hash(new { request.OrderId, request.InspectionType, request.SampleRule, request.SampleUnitIds }));
        return Results.Created($"/api/inspection-lots/{result.Id}", result);
    }

    private static async Task<IResult> AddInspectionResult(string lotId, AddInspectionResultRequest request,
        InspectionRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        lotId = ApiValidation.Required(lotId, "lotId");
        var session = Session(context);
        var values = new
        {
            lotId,
            UnitId = ApiValidation.Required(request.UnitId, "unitId"),
            ItemCode = ApiValidation.Required(request.ItemCode, "itemCode", 64),
            request.Outcome,
            DefectCode = ApiValidation.Optional(request.DefectCode, "defectCode", 64),
            ResponsibleOperationId = ApiValidation.Required(request.ResponsibleOperationId, "responsibleOperationId"),
            Remarks = ApiValidation.Optional(request.Remarks, "remarks", 1024),
            session.UserId
        };
        var result = await repository.AddResultAsync(lotId, values.UnitId, values.ItemCode, values.Outcome,
            values.DefectCode, values.ResponsibleOperationId, values.Remarks, Key(context, request.IdempotencyKey),
            Hash(values), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "InspectionResultAdded", "InspectionLot", lotId, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> CompleteInspectionLot(string lotId, CompleteInspectionLotRequest request,
        InspectionRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        lotId = ApiValidation.Required(lotId, "lotId");
        if (request.ExpectedVersion < 0) throw new ArgumentException("expectedVersion必须大于或等于零。", nameof(request));
        var session = Session(context);
        var key = Key(context, request.IdempotencyKey);
        var result = await repository.CompleteLotAsync(lotId, request.ExpectedVersion, key,
            Hash(new { lotId, request.ExpectedVersion, session.UserId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "InspectionLotCompleted", "InspectionLot", lotId,
                new { Status = "Open", Version = request.ExpectedVersion }, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> ApplyDisposition(string lotId, ApplyDispositionRequest request,
        InspectionRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        lotId = ApiValidation.Required(lotId, "lotId");
        var session = Session(context);
        var reason = ApiValidation.Required(request.ReasonCode, "reasonCode", 128);
        var result = await repository.ApplyDispositionAsync(lotId, request.Decision, reason, session.UserId,
            Key(context, request.IdempotencyKey), Hash(new { lotId, request.Decision, reason, session.UserId,
                request.ReworkRouteId, request.ReworkStartOperationId }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "InspectionDispositionApproved", "InspectionLot", lotId,
                new { Status = "Failed" }, value), request.ReworkRouteId, request.ReworkStartOperationId);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateReworkOrder(CreateReworkOrderRequest request,
        ReworkOrderRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        var order = new ReworkOrder(EntityId.New(), new EntityId(ApiValidation.Required(request.ProductionUnitId, "productionUnitId")),
            new EntityId(ApiValidation.Required(request.RouteId, "routeId")),
            ApiValidation.Required(request.ReasonCode, "reasonCode", 128),
            ApiValidation.Required(request.StartOperationId, "startOperationId"), request.Sequence);
        var result = await repository.CreateAsync(order, cancellationToken,
            value => AuditSnapshot.Create(context, "ReworkOrderCreated", "ReworkOrder", value.Id, null, value),
            Key(context, request.IdempotencyKey), Hash(new { request.ProductionUnitId, request.RouteId,
                request.ReasonCode, request.StartOperationId, request.Sequence }));
        return Results.Created($"/api/rework-orders/{result.Id}", result);
    }

    private static Task<IResult> ApproveReworkOrder(string id, ReworkCommandRequest request, ReworkOrderRepository repository,
        HttpContext context, CancellationToken cancellationToken) =>
        ChangeReworkState(id, "ReworkOrderApproved", request, repository.ApproveAsync, context, cancellationToken);

    private static Task<IResult> ActivateReworkOrder(string id, ReworkCommandRequest request, ReworkOrderRepository repository,
        HttpContext context, CancellationToken cancellationToken) =>
        ChangeReworkState(id, "ReworkOrderActivated", request, repository.ActivateAsync, context, cancellationToken);

    private static Task<IResult> CompleteReworkOrder(string id, ReworkCommandRequest request, ReworkOrderRepository repository,
        HttpContext context, CancellationToken cancellationToken) =>
        ChangeReworkState(id, "ReworkOrderCompleted", request, repository.CompleteAsync, context, cancellationToken);

    private static async Task<IResult> ChangeReworkState(string id, string action, ReworkCommandRequest request,
        Func<string, string, IdempotencyKey, string, DateTimeOffset, CancellationToken,
            Func<ReworkOrderSnapshot, AuditEventSnapshot>?, Task<ReworkOrderSnapshot>> change,
        HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        var session = Session(context);
        var result = await change(id, session.UserId, Key(context, request.IdempotencyKey),
            Hash(new { id, action, session.UserId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, action, "ReworkOrder", id, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateShipment(CreateShipmentRequest request, ShipmentRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var shipment = new Shipment(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
            ApiValidation.Required(request.Customer, "customer"), request.PlannedQuantity,
            ApiValidation.Required(request.DeliveryReference, "deliveryReference", 256));
        var result = await repository.CreateAsync(shipment, DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "ShipmentCreated", "Shipment", value.Id, null, value),
            Key(context, request.IdempotencyKey), Hash(new { request.OrderId, request.Customer,
                request.PlannedQuantity, request.DeliveryReference }));
        return Results.Created($"/api/shipments/{result.Id}", result);
    }

    private static async Task<IResult> AddShipmentCarton(string id, AddShipmentCartonRequest request,
        ShipmentRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        var cartonId = ApiValidation.Required(request.CartonId, "cartonId");
        var session = Session(context);
        var result = await repository.AddCartonAsync(id, cartonId, session.UserId,
            Key(context, request.IdempotencyKey), Hash(new { id, cartonId, session.UserId }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "ShipmentCartonAdded", "Shipment", id, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> ConfirmShipment(string id, ConfirmShipmentRequest request,
        ShipmentRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        var session = Session(context);
        var result = await repository.ConfirmAsync(id, session.UserId, Key(context, request.IdempotencyKey),
            Hash(new { id, session.UserId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "ShipmentConfirmed", "Shipment", id,
                new { Status = "PendingConfirmation" }, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> ArchiveOrder(string orderId, ArchiveOrderRequest request,
        OrderArchiveRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        orderId = ApiValidation.Required(orderId, "orderId");
        var session = Session(context);
        var result = await repository.ArchiveAsync(orderId, session.UserId, Key(context, request.IdempotencyKey),
            Hash(new { orderId, session.UserId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "OrderArchived", "ProductionOrder", orderId,
                null, new { value.Id, value.PayloadHash, value.ArchivedAtUtc }));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetArchive(string orderId, OrderArchiveRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var result = await repository.GetByOrderIdAsync(ApiValidation.Required(orderId, "orderId"), cancellationToken);
        return result == null
            ? Results.NotFound(new ApiError("NOT_FOUND", "订单归档快照不存在。", context.TraceIdentifier))
            : Results.Ok(result);
    }

    private static async Task<IResult> GetDispositionTasks(string? status, InspectionRepository repository,
        CancellationToken cancellationToken) => Results.Ok(await repository.GetDispositionTasksAsync(status, cancellationToken));

    private static async Task<IResult> GetArchiveRepairTasks(string? status, OrderArchiveRepository repository,
        CancellationToken cancellationToken) => Results.Ok(await repository.GetRepairTasksAsync(status, cancellationToken));

    private static async Task<IResult> RepairArchive(string id, RepairArchiveRequest request,
        OrderArchiveRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        var session = Session(context);
        var result = await repository.RepairAndRearchiveAsync(id, session.UserId, Key(context, request.IdempotencyKey),
            Hash(new { id, session.UserId }), DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "OrderArchiveRepaired", "ArchiveRepairTask", id, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> StageCsvImport(string type, CsvExchangeRepository repository,
        HttpContext context, CancellationToken cancellationToken)
    {
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaximumCsvBytes;
        if (context.Request.ContentLength > MaximumCsvBytes)
            throw new ArgumentException("CSV 正文不能超过 10 MB。", nameof(context.Request.Body));
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumCsvBytes)
                throw new ArgumentException("CSV 正文不能超过 10 MB。", nameof(context.Request.Body));
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0) throw new ArgumentException("CSV 正文不能为空。", nameof(context.Request.Body));
        var session = Session(context);
        var source = buffer.ToArray();
        var result = await repository.StageAsync(type, source, session.UserId, Key(context, ""),
            Hash(new { Type = type, Source = Convert.ToHexString(SHA256.HashData(source)) }), DateTimeOffset.UtcNow,
            cancellationToken, value => AuditSnapshot.Create(context, "CsvImportStaged", "CsvImportBatch", value.Id,
                null, value));
        return Results.Created($"/api/csv-imports/{result.Id}", result);
    }

    private static async Task<IResult> GetCsvImport(string id, CsvExchangeRepository repository, HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetAsync(ApiValidation.Required(id, "id"), cancellationToken);
        return result == null ? Results.NotFound(new ApiError("NOT_FOUND", "CSV 导入批次不存在。",
            context.TraceIdentifier)) : Results.Ok(result);
    }

    private static async Task<IResult> ConfirmCsvImport(string id, ConfirmCsvImportRequest request,
        CsvExchangeRepository repository, HttpContext context, CancellationToken cancellationToken)
    {
        id = ApiValidation.Required(id, "id");
        var result = await repository.ConfirmAsync(id, Key(context, request.IdempotencyKey), Hash(new { id }),
            DateTimeOffset.UtcNow, cancellationToken,
            value => AuditSnapshot.Create(context, "CsvImportCommitted", "CsvImportBatch", id, null, value));
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportOrders(CsvExchangeRepository repository, HttpContext context,
        CancellationToken cancellationToken) => Csv(await repository.ExportOrdersAsync(CanReveal(context), cancellationToken),
            "orders.csv");
    private static async Task<IResult> ExportNumberRanges(CsvExchangeRepository repository,
        CancellationToken cancellationToken) => Csv(await repository.ExportRangesAsync(cancellationToken), "number-ranges.csv");
    private static async Task<IResult> ExportTraceability(CsvExchangeRepository repository, HttpContext context,
        CancellationToken cancellationToken) => Csv(await repository.ExportTraceabilityAsync(CanReveal(context), cancellationToken),
            "traceability.csv");

    private static IResult Csv(string content, string fileName) => Results.File(Encoding.UTF8.GetBytes(content),
        "text/csv; charset=utf-8", fileName);
    private static bool CanReveal(HttpContext context) => context.User.IsInRole("QualityManager") ||
        context.User.IsInRole("ArchiveAdministrator");
    private static IdempotencyKey Key(HttpContext context, string fallback)
    {
        var header = context.Request.Headers["Idempotency-Key"].ToString();
        return new IdempotencyKey(string.IsNullOrWhiteSpace(header) ? fallback : header);
    }

    private static StationSession Session(HttpContext context) =>
        context.Items[typeof(StationSession)] as StationSession ?? StationSessionAccessor.Get(context.User);
    private static string Hash<T>(T value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}

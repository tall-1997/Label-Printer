using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.MesApi;
using BarTenderPrinter.Persistence;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAuthentication(StationAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, StationAuthenticationHandler>(StationAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Planner", policy => policy.RequireRole("Planner"));
    options.AddPolicy("ProcessEngineer", policy => policy.RequireRole("ProcessEngineer"));
    options.AddPolicy("NumberAllocator", policy => policy.RequireRole("ProcessEngineer", "ProductionOperator"));
    options.AddPolicy("StationOperator", policy => policy.RequireRole("ProductionOperator", "PackagingOperator"));
    options.AddPolicy("ReprintApprover", policy => policy.RequireRole("PrintSupervisor"));
    options.AddPolicy("ReworkCreator", policy => policy.RequireRole("QualityEngineer"));
    options.AddPolicy("ReworkExecutor", policy => policy.RequireRole("ProductionSupervisor"));
    options.AddPolicy("ReworkApprover", policy => policy.RequireRole("QualityManager"));
    options.AddPolicy("QualityOperator", policy => policy.RequireRole("QualityEngineer", "QualityManager"));
    options.AddPolicy("DispositionApprover", policy => policy.RequireRole("QualityManager"));
    options.AddPolicy("ArchiveOperator", policy => policy.RequireRole("ArchiveAdministrator"));
    options.AddPolicy("ShipmentConfirmer", policy => policy.RequireRole("WarehouseSupervisor"));
    options.AddPolicy("WarehouseOperator", policy => policy.RequireRole("WarehouseOperator", "WarehouseSupervisor"));
});
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("MesDatabase");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:MesDatabase 未配置。");
    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddSingleton<ProductionOrderRepository>();
builder.Services.AddSingleton<PostgresMigrator>();
builder.Services.AddSingleton<NumberRangeRepository>();
builder.Services.AddSingleton<AuditEventRepository>();
builder.Services.AddSingleton<StationPassRepository>();
builder.Services.AddSingleton<PackagingRepository>();
builder.Services.AddSingleton<PrintJobRepository>();
builder.Services.AddSingleton<TraceabilityRepository>();
builder.Services.AddSingleton<ExtendedTraceabilityRepository>();
builder.Services.AddSingleton<InspectionRepository>();
builder.Services.AddSingleton<ReworkOrderRepository>();
builder.Services.AddSingleton<ShipmentRepository>();
builder.Services.AddSingleton<OrderArchiveRepository>();
builder.Services.AddSingleton<StationSessionFilter>();

var app = builder.Build();
using (var migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping))
{
    migrationCancellation.CancelAfter(TimeSpan.FromMinutes(2));
    await app.Services.GetRequiredService<PostgresMigrator>().MigrateAsync(migrationCancellation.Token);
}
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    if (response.StatusCode == StatusCodes.Status400BadRequest)
    {
        await response.WriteAsJsonAsync(new ApiError(
            "VALIDATION_FAILED",
            "请求参数或正文格式无效。",
            statusCodeContext.HttpContext.TraceIdentifier));
    }
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<StationSessionFilter>();
api.MapExtendedEndpoints();
api.MapPost("/orders", async (CreateProductionOrderRequest request, ProductionOrderRepository repository,
    AuditEventRepository auditRepository, HttpContext context, CancellationToken cancellationToken) =>
{
    var order = new ProductionOrder(EntityId.New(),
        ApiValidation.Required(request.OrderNumber, "orderNumber"),
        ApiValidation.Required(request.Customer, "customer"),
        ApiValidation.Required(request.ProductModel, "productModel"),
        ApiValidation.Required(request.Color, "color"),
        request.PlannedQuantity, request.ValidFromUtc, request.ValidToUtc);
    await repository.InsertAsync(order, cancellationToken);
    await auditRepository.AppendAsync(AuditSnapshot.Create(context, "OrderCreated", "ProductionOrder", order.Id.Value,
        null, new { order.OrderNumber, order.Status }), cancellationToken);
    return Results.Created($"/api/orders/{order.Id.Value}", await repository.GetAsync(order.Id.Value, cancellationToken));
}).RequireAuthorization("Planner");

api.MapGet("/orders/{id}", async (string id, ProductionOrderRepository repository, HttpContext context,
    CancellationToken cancellationToken) =>
{
    var order = await repository.GetAsync(ApiValidation.Required(id, "id"), cancellationToken);
    return order == null
        ? Results.NotFound(new ApiError("NOT_FOUND", "生产订单不存在。", context.TraceIdentifier))
        : Results.Ok(order);
});

api.MapPost("/number-ranges", async (CreateNumberRangeRequest request, NumberRangeRepository repository,
    AuditEventRepository auditRepository, HttpContext context, CancellationToken cancellationToken) =>
{
    var range = new NumberRange(EntityId.New(), new EntityId(ApiValidation.Required(request.OrderId, "orderId")),
        request.NumberType, ApiValidation.Optional(request.Prefix, "prefix", 64), request.DatePattern,
        request.Start, request.End, request.Step, request.NumericWidth, ApiValidation.RegexPattern(request.ValidationPattern));
    await repository.InsertAsync(range, cancellationToken);
    await auditRepository.AppendAsync(AuditSnapshot.Create(context, "NumberRangeCreated", "NumberRange", range.Id.Value,
        null, new { range.OrderId, range.Type, range.Start, range.End }), cancellationToken);
    return Results.Created($"/api/number-ranges/{range.Id.Value}", await repository.GetAsync(range.Id.Value, cancellationToken));
}).RequireAuthorization("ProcessEngineer");

api.MapGet("/number-ranges/{id}", async (string id, NumberRangeRepository repository, HttpContext context,
    CancellationToken cancellationToken) =>
{
    var range = await repository.GetAsync(ApiValidation.Required(id, "id"), cancellationToken);
    return range == null
        ? Results.NotFound(new ApiError("NOT_FOUND", "号段不存在。", context.TraceIdentifier))
        : Results.Ok(range);
});

api.MapPost("/number-ranges/{id}/allocations", async (string id, AllocateNumberRequest request,
    NumberRangeRepository repository, AuditEventRepository auditRepository, HttpContext context,
    CancellationToken cancellationToken) =>
{
    id = ApiValidation.Required(id, "id");
    var key = new IdempotencyKey(request.IdempotencyKey);
    var session = GetSession(context);
    var operatorId = session.UserId;
    var stationId = session.StationId;
    var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { RangeId = id, key.Value, operatorId, stationId })))).ToLowerInvariant();
    var result = await repository.AllocateAsync(id, key, requestHash, stationId, operatorId,
        DateTimeOffset.UtcNow, cancellationToken);
    if (!result.IsReplay)
        await auditRepository.AppendAsync(AuditSnapshot.Create(context, "NumberAllocated", "NumberRange", id, null,
            new { result.Id, result.Status, result.Value }), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("NumberAllocator");

api.MapPost("/station-passes", async (StationPassRequest request, StationPassRepository repository,
    AuditEventRepository auditRepository, HttpContext context, CancellationToken cancellationToken) =>
{
    var unitId = ApiValidation.Required(request.UnitId, "unitId");
    var orderId = ApiValidation.Required(request.OrderId, "orderId");
    var routeId = ApiValidation.Required(request.RouteId, "routeId");
    var operationId = ApiValidation.Required(request.OperationId, "operationId");
    var reworkOrderId = ApiValidation.Optional(request.ReworkOrderId, "reworkOrderId", 128);
    if (request.ReworkSequence < 0)
        throw new ArgumentException("reworkSequence必须大于或等于零。", nameof(request.ReworkSequence));
    var key = new IdempotencyKey(request.IdempotencyKey);
    var session = GetSession(context);
    var operatorId = session.UserId;
    var stationId = session.StationId;
    var requestHash = HashRequest(new
    {
        unitId, orderId, routeId, operationId, stationId, operatorId, reworkOrderId, request.ReworkSequence
    });
    var result = await repository.PassAsync(unitId, orderId, routeId, operationId, stationId, operatorId,
        key, requestHash, DateTimeOffset.UtcNow, reworkOrderId, request.ReworkSequence, cancellationToken);
    if (!result.IsReplay)
        await auditRepository.AppendAsync(AuditSnapshot.Create(context, "StationPassed", "ProductionUnit", unitId,
            null, new { result.RouteId, result.OperationId, result.Id }), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("StationOperator");

api.MapPost("/packaging-bindings", async (BindPackagingRequest request, PackagingRepository repository,
    AuditEventRepository auditRepository, HttpContext context, CancellationToken cancellationToken) =>
{
    var parentId = ApiValidation.Required(request.ParentId, "parentId");
    var childId = ApiValidation.Required(request.ChildId, "childId");
    if (request.ExpectedParentVersion < 0)
        throw new ArgumentException("expectedParentVersion必须大于或等于零。", nameof(request.ExpectedParentVersion));
    var key = new IdempotencyKey(request.IdempotencyKey);
    var operatorId = GetSession(context).UserId;
    var requestHash = HashRequest(new { parentId, childId, request.ExpectedParentVersion, operatorId });
    var result = await repository.BindPackagingAsync(parentId, childId, request.ExpectedParentVersion, operatorId,
        DateTimeOffset.UtcNow, key, requestHash, cancellationToken);
    if (!result.IsReplay)
        await auditRepository.AppendAsync(AuditSnapshot.Create(context, "PackagingBound", "PackagingUnit", parentId,
            null, new { result.ChildId, result.ParentVersion, result.ParentClosed,
                PrintIntentId = result.PrintIntent?.Id }), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("StationOperator");

api.MapPost("/print-jobs/claims", async (ClaimPrintJobRequest request, PrintJobRepository repository,
    AuditEventRepository auditRepository, HttpContext context, CancellationToken cancellationToken) =>
{
    var key = new IdempotencyKey(request.IdempotencyKey);
    var session = GetSession(context);
    var operatorId = session.UserId;
    var stationId = session.StationId;
    var requestHash = HashRequest(new { stationId, operatorId });
    var result = await repository.ClaimNextAsync(stationId, operatorId, key, requestHash,
        DateTimeOffset.UtcNow, cancellationToken);
    if (result.Job == null) return Results.NoContent();
    if (!result.IsReplay)
        await auditRepository.AppendAsync(AuditSnapshot.Create(context, "PrintJobClaimed", "PrintJob", result.Job.JobId,
            new { State = "Received" }, new { result.Job.State, stationId }), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("StationOperator");

api.MapPost("/print-jobs/{jobId}/receipts", async (string jobId, PrintJobReceiptRequest request,
    PrintJobRepository repository, AuditEventRepository auditRepository, HttpContext context,
    CancellationToken cancellationToken) =>
{
    jobId = ApiValidation.Required(jobId, "jobId");
    var state = ApiValidation.Required(request.State, "state");
    if (state is not ("Submitted" or "Failed" or "Uncertain"))
        throw new ArgumentException("state必须为Submitted、Failed或Uncertain。", nameof(request.State));
    if (request.Result.ValueKind != JsonValueKind.Object)
        throw new ArgumentException("result必须为JSON对象。", nameof(request.Result));
    var resultJson = request.Result.GetRawText();
    if (Encoding.UTF8.GetByteCount(resultJson) > 65536)
        throw new ArgumentException("result不能超过65536字节。", nameof(request.Result));
    var key = new IdempotencyKey(request.IdempotencyKey);
    var stationId = GetSession(context).StationId;
    var requestHash = HashRequest(new { jobId, stationId, state, resultJson });
    var result = await repository.RecordReceiptAsync(jobId, stationId, key, requestHash, state, resultJson,
        DateTimeOffset.UtcNow, cancellationToken);
    if (!result.IsReplay)
        await auditRepository.AppendAsync(AuditSnapshot.Create(context, "PrintJobReceiptRecorded", "PrintJob", jobId,
            new { State = "Submitting" }, new { result.Job.State, result.Job.ResultJson }), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("StationOperator");

api.MapGet("/print-jobs/{jobId}", async (string jobId, PrintJobRepository repository, HttpContext context,
    CancellationToken cancellationToken) =>
{
    var job = await repository.GetByJobIdAsync(ApiValidation.Required(jobId, "jobId"), cancellationToken);
    return job == null
        ? Results.NotFound(new ApiError("NOT_FOUND", "打印作业不存在。", context.TraceIdentifier))
        : Results.Ok(job);
});

api.MapGet("/print-jobs/by-idempotency-key/{key}", async (string key, PrintJobRepository repository,
    HttpContext context, CancellationToken cancellationToken) =>
{
    var job = await repository.GetByIdempotencyKeyAsync(new IdempotencyKey(key).Value, cancellationToken);
    return job == null
        ? Results.NotFound(new ApiError("NOT_FOUND", "打印作业不存在。", context.TraceIdentifier))
        : Results.Ok(job);
});

api.MapGet("/traceability", async (string type, string value, ExtendedTraceabilityRepository repository,
    HttpContext context, CancellationToken cancellationToken) =>
{
    type = ApiValidation.Required(type, "type", 32);
    value = ApiValidation.Required(value, "value", 128);
    if (!Enum.TryParse<TraceabilityQueryType>(type, true, out var queryType) ||
        !Enum.IsDefined(queryType))
        throw new ArgumentException("type必须为Order、Imei、SerialNumber、Carton或Pallet。", nameof(type));
    var result = await repository.QueryAsync(queryType, value, cancellationToken);
    return result == null
        ? Results.NotFound(new ApiError("NOT_FOUND", "未找到关联生产履历。", context.TraceIdentifier))
        : Results.Ok(result);
});

app.Run();

static StationSession GetSession(HttpContext context) =>
    context.Items[typeof(StationSession)] as StationSession ?? StationSessionAccessor.Get(context.User);

static string HashRequest<T>(T value) => Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();

public partial class Program
{
}

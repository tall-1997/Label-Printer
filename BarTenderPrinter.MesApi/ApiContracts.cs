using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Quality;
using System.Text.Json;

namespace BarTenderPrinter.MesApi;

public sealed record ApiError(string Code, string Message, string CorrelationId, bool Retryable = false, object? Details = null);

public sealed record CreateProductionOrderRequest(
    string OrderNumber,
    string Customer,
    string ProductModel,
    string Color,
    int PlannedQuantity,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc);

public sealed record CreateNumberRangeRequest(
    string OrderId,
    NumberType NumberType,
    string Prefix,
    NumberDatePattern DatePattern,
    long Start,
    long End,
    long Step,
    int NumericWidth,
    string ValidationPattern);

public sealed record AllocateNumberRequest(string IdempotencyKey);

public sealed record StationPassRequest(
    string UnitId,
    string OrderId,
    string RouteId,
    string OperationId,
    string IdempotencyKey,
    string ReworkOrderId = "",
    int ReworkSequence = 0);

public sealed record BindPackagingRequest(
    string ParentId,
    string ChildId,
    long ExpectedParentVersion,
    string IdempotencyKey);

public sealed record ClaimPrintJobRequest(string IdempotencyKey);

public sealed record PrintJobReceiptRequest(
    string IdempotencyKey,
    string State,
    JsonElement Result);

public sealed record CreateInspectionLotRequest(string OrderId, string InspectionType, string SampleRule,
    IReadOnlyList<string> SampleUnitIds);
public sealed record AddInspectionResultRequest(string UnitId, string ItemCode, InspectionOutcome Outcome,
    string DefectCode, string ResponsibleOperationId, string Remarks, string IdempotencyKey);
public sealed record CompleteInspectionLotRequest(long ExpectedVersion, string IdempotencyKey = "");
public sealed record ApplyDispositionRequest(DispositionDecision Decision, string ReasonCode, string IdempotencyKey);

public sealed record CreateReworkOrderRequest(string ProductionUnitId, string RouteId, string ReasonCode,
    string StartOperationId, int Sequence);
public sealed record ReworkCommandRequest(string IdempotencyKey);

public sealed record CreateShipmentRequest(string OrderId, string Customer, int PlannedQuantity,
    string DeliveryReference);
public sealed record AddShipmentCartonRequest(string CartonId, string IdempotencyKey);
public sealed record ConfirmShipmentRequest(string IdempotencyKey);
public sealed record ArchiveOrderRequest(string IdempotencyKey);

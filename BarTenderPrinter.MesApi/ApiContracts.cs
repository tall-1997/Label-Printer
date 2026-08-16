using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Quality;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
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
public sealed record TransitionOrderRequest(ProductionOrderStatus TargetStatus, long ExpectedVersion,
    string IdempotencyKey);
public sealed record CreateProductionUnitRequest(string OrderId,
    IReadOnlyDictionary<NumberType, string> AllocationIds, string IdempotencyKey);
public sealed record CreateRouteRequest(string OrderId, string Name, RouteType RouteType,
    IReadOnlyList<CreateOperationRequest> Operations, string IdempotencyKey);
public sealed record CreateOperationRequest(string Id, string Name, int Sequence);
public sealed record CreateStationRequest(string Name, IReadOnlyList<string> QualifiedOperationIds,
    string IdempotencyKey);
public sealed record CreatePackagingUnitRequest(string OrderId, PackagingUnitType UnitType, string Code,
    string ProductModel, string Color, int Capacity, string? ProductionUnitId, string IdempotencyKey);
public sealed record ChangeNumberStatusRequest(NumberAllocationStatus TargetStatus, string ReasonCode,
    string IdempotencyKey);
public sealed record CreateWeightRuleRequest(string OrderId, PackagingUnitType PackagingUnitType,
    decimal MinimumWeight, decimal MaximumWeight, string Unit, string IdempotencyKey);
public sealed record RecordWeightRequest(decimal Weight, string Unit, string DeviceId, bool IsSimulated,
    string IdempotencyKey);
public sealed record CreateIdentifierWriteTaskRequest(string UnitId, IReadOnlyList<string> AllocationIds,
    string Platform, string TargetStationId, string IdempotencyKey);
public sealed record ClaimIdentifierWriteTaskRequest(string Platform, string IdempotencyKey);
public sealed record IdentifierWriteResultRequest(IdentifierWriteTaskState State, JsonElement Result,
    string DiagnosticCode, string IdempotencyKey);

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
    IReadOnlyList<string> SampleUnitIds, string IdempotencyKey = "");
public sealed record AddInspectionResultRequest(string UnitId, string ItemCode, InspectionOutcome Outcome,
    string DefectCode, string ResponsibleOperationId, string Remarks, string IdempotencyKey);
public sealed record CompleteInspectionLotRequest(long ExpectedVersion, string IdempotencyKey = "");
public sealed record ApplyDispositionRequest(DispositionDecision Decision, string ReasonCode, string IdempotencyKey,
    string ReworkRouteId = "", string ReworkStartOperationId = "");

public sealed record CreateReworkOrderRequest(string ProductionUnitId, string RouteId, string ReasonCode,
    string StartOperationId, int Sequence, string IdempotencyKey = "");
public sealed record ReworkCommandRequest(string IdempotencyKey);

public sealed record CreateShipmentRequest(string OrderId, string Customer, int PlannedQuantity,
    string DeliveryReference, string IdempotencyKey = "");
public sealed record AddShipmentCartonRequest(string CartonId, string IdempotencyKey);
public sealed record ConfirmShipmentRequest(string IdempotencyKey);
public sealed record ArchiveOrderRequest(string IdempotencyKey);
public sealed record ConfirmCsvImportRequest(string IdempotencyKey = "");
public sealed record RepairArchiveRequest(string IdempotencyKey = "");

namespace BarTenderPrinter.Persistence;

public sealed record ProductionOrderSnapshot(
    string Id,
    string OrderNumber,
    string Customer,
    string ProductModel,
    string Color,
    int PlannedQuantity,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string Status,
    long Version,
    int CompletedQuantity,
    int ExceptionQuantity);

public sealed record NumberRangeSnapshot(
    string Id,
    string OrderId,
    string NumberType,
    string Prefix,
    string DatePattern,
    long StartValue,
    long EndValue,
    long NextValue,
    long Step,
    int NumericWidth,
    string ValidationPattern,
    bool IsExhausted,
    long Version);

public sealed record NumberAllocationResult(string Id, string Value, string Status, bool IsReplay);

public sealed record NumberAllocationStatusSnapshot(string AllocationId, string Value, string Status,
    string UnitId, bool IsReplay);

public sealed record NumberAllocationHistorySnapshot(string Id, string AllocationId, string PreviousStatus,
    string NextStatus, string ReasonCode, string ActorId, string StationId, DateTimeOffset ChangedAtUtc);

public sealed record ProductionUnitSnapshot(
    string Id,
    string OrderId,
    string Status,
    string CurrentOperationId,
    long Version);

public sealed record ManufacturingRouteSnapshot(string Id, string OrderId, string Name, string RouteType,
    IReadOnlyList<ManufacturingOperationSnapshot> Operations);
public sealed record ManufacturingOperationSnapshot(string Id, string Name, int Sequence);
public sealed record StationSnapshot(string Id, string Name, IReadOnlyList<string> QualifiedOperationIds);
public sealed record PackagingUnitSnapshot(string Id, string OrderId, string UnitType, string Code,
    string ProductModel, string Color, int Capacity, string Status, long Version, string? ProductionUnitId);

public sealed record WeightRuleSnapshot(string Id, string OrderId, string PackagingUnitType,
    decimal MinimumWeight, decimal MaximumWeight, string Unit, long Version);
public sealed record WeightMeasurementSnapshot(string Id, string PackagingUnitId, string RuleId,
    decimal Weight, string Unit, string DeviceId, bool IsSimulated, string Result,
    decimal MinimumWeight, decimal MaximumWeight, DateTimeOffset MeasuredAtUtc, bool IsReplay);

public sealed record IdentifierWriteTaskSnapshot(string Id, string UnitId, string[] AllocationIds,
    string IdentifiersJson, string Platform, string TargetStationId, string State, string ClaimedByStationId,
    string ClaimedByOperatorId, string? ResultJson, string DiagnosticCode, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, bool IsReplay = false);
public sealed record IdentifierWriteClaimResult(IdentifierWriteTaskSnapshot? Task, bool IsReplay);

public sealed record StationPassSnapshot(
    string Id,
    string UnitId,
    string OrderId,
    string RouteId,
    string OperationId,
    string StationId,
    string OperatorId,
    DateTimeOffset OccurredAtUtc,
    string IdempotencyKey,
    string ReworkOrderId,
    int ReworkSequence,
    bool IsReplay);

public sealed record PackagingPrintIntentSnapshot(
    string Id,
    string PackagingUnitId,
    string LabelType,
    string FieldsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record PackagingBindingResult(
    string ParentId,
    string ChildId,
    long ParentVersion,
    bool ParentClosed,
    bool IsReplay,
    PackagingPrintIntentSnapshot? PrintIntent = null);

public sealed record PrintJobSnapshot(
    string JobId,
    string IdempotencyKey,
    string LabelType,
    string TemplateId,
    string TemplateVersion,
    string State,
    string RequestHash,
    string RequestJson,
    string? ResultJson,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string ClaimedByStationId = "",
    string ClaimedByOperatorId = "",
    string ClaimIdempotencyKey = "",
    string? ReceiptIdempotencyKey = null,
    string? TraceOrderId = null,
    string? TraceUnitId = null,
    string? TracePackagingUnitId = null);

public sealed record PrintJobClaimResult(PrintJobSnapshot? Job, bool IsReplay);

public sealed record PrintJobReceiptResult(PrintJobSnapshot Job, bool IsReplay);

public enum TraceabilityQueryType
{
    Order,
    Imei,
    SerialNumber,
    Carton,
    Pallet
}

public sealed record TraceProductionUnitSnapshot(
    string Id,
    string OrderId,
    string Status,
    string CurrentOperationId,
    string IdentifiersJson,
    long Version);

public sealed record TracePackagingUnitSnapshot(
    string Id,
    string OrderId,
    string Type,
    string Code,
    string ProductModel,
    string Color,
    int Capacity,
    string Status,
    long Version,
    string? ProductionUnitId);

public sealed record TracePackagingBindingSnapshot(
    string ParentId,
    string ChildId,
    DateTimeOffset BoundAtUtc,
    string OperatorId,
    bool IsActive);

public sealed record TraceabilitySnapshot(
    TraceabilityQueryType QueryType,
    string QueryValue,
    ProductionOrderSnapshot Order,
    IReadOnlyList<TraceProductionUnitSnapshot> ProductionUnits,
    IReadOnlyList<StationPassSnapshot> StationPasses,
    IReadOnlyList<TracePackagingUnitSnapshot> PackagingUnits,
    IReadOnlyList<TracePackagingBindingSnapshot> PackagingBindings,
    IReadOnlyList<PackagingPrintIntentSnapshot> PrintIntents,
    IReadOnlyList<PrintJobSnapshot> PrintJobs,
    IReadOnlyList<AuditEventSnapshot> AuditEvents);

public sealed record AuditEventSnapshot(
    string Id,
    string ActorId,
    string StationId,
    string ShiftId,
    string CorrelationId,
    string Action,
    string EntityType,
    string EntityId,
    string? BeforeJson,
    string? AfterJson,
    DateTimeOffset OccurredAtUtc);

public sealed record InspectionLotSnapshot(string Id, string OrderId, string InspectionType, string SampleRule,
    string SampleUnitIdsJson, string Status, long Version, DateTimeOffset CreatedAtUtc, bool IsReplay = false);

public sealed record InspectionResultSnapshot(string Id, string LotId, string UnitId, string ItemCode,
    string Outcome, string DefectCode, string ResponsibleOperationId, string Remarks,
    DateTimeOffset InspectedAtUtc, bool IsReplay);

public sealed record DispositionSnapshot(string Id, string LotId, string Decision, string ReasonCode,
    string ApprovedBy, DateTimeOffset ApprovedAtUtc, bool IsReplay);

public sealed record DispositionTaskSnapshot(string Id, string LotId, string Status, DateTimeOffset CreatedAtUtc,
    string CompletedBy, DateTimeOffset? CompletedAtUtc, string? DispositionId = null);

public sealed record ReworkOrderSnapshot(string Id, string ProductionUnitId, string RouteId, string ReasonCode,
    string StartOperationId, string Status, int Sequence, string ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    string ClosedBy, DateTimeOffset? ClosedAtUtc, long Version, bool IsReplay = false);

public sealed record ShipmentSnapshot(string Id, string OrderId, string Customer, int PlannedQuantity,
    string DeliveryReference, string Status, long Version, int ActualQuantity, DateTimeOffset CreatedAtUtc,
    bool IsReplay = false);

public sealed record ShipmentItemSnapshot(string ShipmentId, string CartonId, int Quantity,
    DateTimeOffset ScannedAtUtc, string OperatorId, bool IsReplay);

public sealed record OrderArchiveSnapshotRecord(string Id, string OrderId, string PayloadJson, string PayloadHash,
    DateTimeOffset ArchivedAtUtc, string ArchivedBy, bool IsReplay);

public sealed record ArchiveRepairTaskSnapshot(string Id, string OrderId, string ArchiveId, string ExpectedHash,
    string ActualHash, string Status, DateTimeOffset CreatedAtUtc, string RepairedBy,
    DateTimeOffset? RepairedAtUtc, string? ReplacementArchiveId, bool IsReplay = false);

public sealed record CsvImportErrorSnapshot(int RowNumber, string Code, string Message);
public sealed record CsvImportBatchSnapshot(string Id, string ImportType, string SourceSha256, string Status,
    int TotalRows, int ValidRows, IReadOnlyList<CsvImportErrorSnapshot> Errors, DateTimeOffset CreatedAtUtc,
    string CreatedBy, DateTimeOffset? ConfirmedAtUtc = null, bool IsReplay = false);

public sealed record ExtendedTraceabilitySnapshot(
    TraceabilityQueryType QueryType,
    string QueryValue,
    ProductionOrderSnapshot Order,
    IReadOnlyList<TraceProductionUnitSnapshot> ProductionUnits,
    IReadOnlyList<StationPassSnapshot> StationPasses,
    IReadOnlyList<TracePackagingUnitSnapshot> PackagingUnits,
    IReadOnlyList<TracePackagingBindingSnapshot> PackagingBindings,
    IReadOnlyList<PackagingPrintIntentSnapshot> PrintIntents,
    IReadOnlyList<PrintJobSnapshot> PrintJobs,
    IReadOnlyList<AuditEventSnapshot> AuditEvents,
    IReadOnlyList<InspectionLotSnapshot> InspectionLots,
    IReadOnlyList<InspectionResultSnapshot> InspectionResults,
    IReadOnlyList<DispositionSnapshot> Dispositions,
    IReadOnlyList<ReworkOrderSnapshot> ReworkOrders,
    IReadOnlyList<ShipmentSnapshot> Shipments,
    IReadOnlyList<ShipmentItemSnapshot> ShipmentItems,
    IReadOnlyList<OrderArchiveSnapshotRecord> Archives);

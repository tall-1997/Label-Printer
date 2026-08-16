using System;
using System.Collections.Generic;
using System.Text.Json;

namespace BarTenderPrinter
{
    public sealed class MesConnectionOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:5000";
        public int TimeoutSeconds { get; set; } = 10;
        public int MaxRetries { get; set; } = 2;

        public MesConnectionOptions Normalize()
        {
            if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("MES 服务地址必须是有效的 HTTP 或 HTTPS 地址。", nameof(BaseUrl));
            BaseUrl = uri.ToString().TrimEnd('/');
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 120);
            MaxRetries = Math.Clamp(MaxRetries, 0, 3);
            return this;
        }

        public MesConnectionOptions Snapshot() => new MesConnectionOptions
        {
            BaseUrl = BaseUrl,
            TimeoutSeconds = TimeoutSeconds,
            MaxRetries = MaxRetries
        }.Normalize();
    }

    public sealed class MesApiError
    {
        public string Code { get; set; } = "MES_REQUEST_FAILED";
        public string Message { get; set; } = "MES 请求失败。";
        public string CorrelationId { get; set; } = "";
        public bool Retryable { get; set; }
    }

    public sealed class MesResult<T>
    {
        public bool IsSuccess { get; set; }
        public T Value { get; set; }
        public MesApiError Error { get; set; }
        public string CorrelationId { get; set; } = "";
        public int StatusCode { get; set; }

        public static MesResult<T> Success(T value, string correlationId, int statusCode = 200) =>
            new MesResult<T> { IsSuccess = true, Value = value, CorrelationId = correlationId, StatusCode = statusCode };

        public static MesResult<T> Failure(MesApiError error, int statusCode = 0) =>
            new MesResult<T> { Error = error, CorrelationId = error?.CorrelationId ?? "", StatusCode = statusCode };
    }

    public sealed class MesOrderSnapshot
    {
        public string Id { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string Customer { get; set; } = "";
        public string ProductModel { get; set; } = "";
        public string Color { get; set; } = "";
        public int PlannedQuantity { get; set; }
        public string Status { get; set; } = "";
        public int CompletedQuantity { get; set; }
        public int ExceptionQuantity { get; set; }
    }

    public sealed class MesStationPassRequest
    {
        public string UnitId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public string RouteId { get; set; } = "";
        public string OperationId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string ReworkOrderId { get; set; } = "";
        public int ReworkSequence { get; set; }
    }

    public sealed class MesPackagingBindRequest
    {
        public string ParentId { get; set; } = "";
        public string ChildId { get; set; } = "";
        public long ExpectedParentVersion { get; set; }
        public string IdempotencyKey { get; set; } = "";
    }

    public class MesIdempotentRequest
    {
        public string IdempotencyKey { get; set; } = "";
    }

    public sealed class MesOrderTransitionRequest : MesIdempotentRequest
    {
        public string TargetStatus { get; set; } = "Published";
        public long ExpectedVersion { get; set; }
    }

    public sealed class MesProductionUnitRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public IReadOnlyDictionary<string, string> AllocationIds { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class MesRouteOperationRequest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Sequence { get; set; }
    }

    public sealed class MesRouteRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RouteType { get; set; } = "Standard";
        public IReadOnlyList<MesRouteOperationRequest> Operations { get; set; } = Array.Empty<MesRouteOperationRequest>();
    }

    public sealed class MesStationRequest : MesIdempotentRequest
    {
        public string Name { get; set; } = "";
        public IReadOnlyList<string> QualifiedOperationIds { get; set; } = Array.Empty<string>();
    }

    public sealed class MesPackagingUnitRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public string UnitType { get; set; } = "Carton";
        public string Code { get; set; } = "";
        public string ProductModel { get; set; } = "";
        public string Color { get; set; } = "";
        public int Capacity { get; set; }
        public string ProductionUnitId { get; set; }
    }

    public sealed class MesNumberStatusRequest : MesIdempotentRequest
    {
        public string TargetStatus { get; set; } = "Frozen";
        public string ReasonCode { get; set; } = "";
    }

    public sealed class MesWeightRuleRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public string PackagingUnitType { get; set; } = "Carton";
        public decimal MinimumWeight { get; set; }
        public decimal MaximumWeight { get; set; }
        public string Unit { get; set; } = "kg";
    }

    public sealed class MesWeightRequest : MesIdempotentRequest
    {
        public decimal Weight { get; set; }
        public string Unit { get; set; } = "kg";
        public string DeviceId { get; set; } = "simulated-scale";
        public bool IsSimulated { get; set; } = true;
    }

    public sealed class MesIdentifierWriteTaskRequest : MesIdempotentRequest
    {
        public string UnitId { get; set; } = "";
        public IReadOnlyList<string> AllocationIds { get; set; } = Array.Empty<string>();
        public string Platform { get; set; } = "android";
        public string TargetStationId { get; set; } = "";
    }

    public sealed class MesIdentifierWriteClaimRequest : MesIdempotentRequest
    {
        public string Platform { get; set; } = "android";
    }

    public sealed class MesIdentifierWriteResultRequest : MesIdempotentRequest
    {
        public string State { get; set; } = "Succeeded";
        public JsonElement Result { get; set; }
        public string DiagnosticCode { get; set; } = "";
    }

    public sealed class MesInspectionLotRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public string InspectionType { get; set; } = "OQC";
        public string SampleRule { get; set; } = "";
        public IReadOnlyList<string> SampleUnitIds { get; set; } = Array.Empty<string>();
    }

    public sealed class MesInspectionResultRequest : MesIdempotentRequest
    {
        public string UnitId { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string Outcome { get; set; } = "Passed";
        public string DefectCode { get; set; } = "";
        public string ResponsibleOperationId { get; set; } = "";
        public string Remarks { get; set; } = "";
    }

    public sealed class MesInspectionCompleteRequest : MesIdempotentRequest
    {
        public long ExpectedVersion { get; set; }
    }

    public sealed class MesInspectionDispositionRequest : MesIdempotentRequest
    {
        public string Decision { get; set; } = "Release";
        public string ReasonCode { get; set; } = "";
    }

    public sealed class MesReworkOrderRequest : MesIdempotentRequest
    {
        public string ProductionUnitId { get; set; } = "";
        public string RouteId { get; set; } = "";
        public string ReasonCode { get; set; } = "";
        public string StartOperationId { get; set; } = "";
        public int Sequence { get; set; }
    }

    public sealed class MesShipmentRequest : MesIdempotentRequest
    {
        public string OrderId { get; set; } = "";
        public string Customer { get; set; } = "";
        public int PlannedQuantity { get; set; }
        public string DeliveryReference { get; set; } = "";
    }

    public sealed class MesShipmentCartonRequest : MesIdempotentRequest
    {
        public string CartonId { get; set; } = "";
    }

    public sealed class MesPrintJob
    {
        public string JobId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string LabelType { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string TemplateVersion { get; set; } = "";
        public string State { get; set; } = "";
        public string RequestJson { get; set; } = "";
        public string ResultJson { get; set; } = "";
        public DateTimeOffset UpdatedAtUtc { get; set; }

        public string ReadRequestString(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(RequestJson)) return "";
            try
            {
                using var document = JsonDocument.Parse(RequestJson);
                return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
            }
            catch (JsonException) { return ""; }
        }
    }

    public sealed class MesPrintClaimResult
    {
        public MesPrintJob Job { get; set; }
        public bool IsReplay { get; set; }
    }

    public sealed class MesPrintRecoveryResult
    {
        public int RecoveredCount { get; set; }
        public IReadOnlyList<MesPrintJob> PrintableJobs { get; set; } = Array.Empty<MesPrintJob>();
    }

    public sealed class MesPrintRequestSnapshot
    {
        public string TemplatePath { get; private set; } = "";
        public string Printer { get; private set; } = "";
        public int Copies { get; private set; } = 1;
        public string BatchId { get; private set; } = "";
        public string BatchItemId { get; private set; } = "";
        public IReadOnlyDictionary<string, string> Fields { get; private set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static MesPrintRequestSnapshot Parse(string requestJson)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(requestJson) ? "{}" : requestJson);
            var root = document.RootElement;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            root.TryGetProperty("fieldValues", out var fieldValues);
            if (fieldValues.ValueKind != JsonValueKind.Object)
                root.TryGetProperty("fields", out fieldValues);
            if (fieldValues.ValueKind == JsonValueKind.Object)
                foreach (var property in fieldValues.EnumerateObject())
                    fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? "" : property.Value.ToString();

            var copies = root.TryGetProperty("copies", out var copiesElement) && copiesElement.TryGetInt32(out var value)
                ? Math.Max(1, value) : 1;
            return new MesPrintRequestSnapshot
            {
                TemplatePath = ReadString(root, "templatePath"),
                Printer = ReadString(root, "printer"),
                Copies = copies,
                BatchId = ReadString(root, "batchId"),
                BatchItemId = ReadString(root, "batchItemId"),
                Fields = fields
            };
        }

        private static string ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
    }

    public enum MesPendingState
    {
        Pending,
        Synced,
        ReviewRequired
    }

    public sealed class MesPendingOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "";
        public string BusinessId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string RequestJson { get; set; } = "";
        public string RequestPath { get; set; } = "";
        public string LocalResultJson { get; set; } = "";
        public string CenterResultJson { get; set; } = "";
        public string ReceiptKey { get; set; } = "";
        public string ReceiptPayloadJson { get; set; } = "";
        public MesPendingState State { get; set; }
        public string ErrorCode { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string ReviewNote { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public interface IMesClientLog
    {
        void Info(string message);
        void Warn(string message);
    }

    public interface IMesApiClient : IDisposable
    {
        MesConnectionOptions Options { get; }
        void Configure(MesConnectionOptions options, string accessToken);
        System.Threading.Tasks.Task<MesResult<T>> GetAsync<T>(string path, System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<MesResult<T>> PostAsync<T>(string path, object request, string idempotencyKey,
            System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<MesResult<T>> PostBytesAsync<T>(string path, byte[] request, string contentType,
            string idempotencyKey, System.Threading.CancellationToken cancellationToken = default);
    }
}

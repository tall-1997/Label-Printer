using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class MesConnectionOptionsStore
    {
        private readonly string _path;
        public MesConnectionOptionsStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

        public MesConnectionOptions Load()
        {
            if (!File.Exists(_path)) return new MesConnectionOptions();
            return (JsonSerializer.Deserialize<MesConnectionOptions>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("MES 连接配置文件内容无效。")).Normalize();
        }

        public void Save(MesConnectionOptions options)
        {
            options = (options ?? throw new ArgumentNullException(nameof(options))).Normalize();
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
    }

    public sealed class MesPendingOperationStore
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private string _loadError = "";
        public MesPendingOperationStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

        public string LoadError { get { lock (_gate) return _loadError; } }

        public IReadOnlyList<MesPendingOperation> GetAll()
        {
            lock (_gate)
            {
                try
                {
                    var items = Read().Select(Clone).ToList();
                    _loadError = "";
                    return items;
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
                {
                    _loadError = $"MES_PENDING_STORE_CORRUPT: {ex.Message} 文件: {_path}";
                    return Array.Empty<MesPendingOperation>();
                }
            }
        }

        public MesPendingOperation Upsert(MesPendingOperation operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            lock (_gate)
            {
                var items = Read();
                var existing = items.FirstOrDefault(item => string.Equals(item.Kind, operation.Kind, StringComparison.Ordinal) &&
                    string.Equals(item.IdempotencyKey, operation.IdempotencyKey, StringComparison.Ordinal));
                var stored = existing ?? operation;
                if (existing == null) items.Add(operation);
                else Copy(operation, existing);
                stored.UpdatedAtUtc = DateTimeOffset.UtcNow;
                Write(items);
                return Clone(stored);
            }
        }

        public void Update(string id, Action<MesPendingOperation> update)
        {
            lock (_gate)
            {
                var items = Read();
                var item = items.FirstOrDefault(value => value.Id == id);
                if (item == null) return;
                update(item);
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                Write(items);
            }
        }

        private List<MesPendingOperation> Read()
        {
            if (!File.Exists(_path)) return new List<MesPendingOperation>();
            return JsonSerializer.Deserialize<List<MesPendingOperation>>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("MES 待处理记录文件内容无效。");
        }

        private void Write(List<MesPendingOperation> items)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }

        private static MesPendingOperation Clone(MesPendingOperation item) =>
            JsonSerializer.Deserialize<MesPendingOperation>(JsonSerializer.Serialize(item));

        private static void Copy(MesPendingOperation source, MesPendingOperation target)
        {
            target.BusinessId = source.BusinessId;
            target.RequestJson = source.RequestJson;
            target.RequestPath = source.RequestPath;
            target.LocalResultJson = source.LocalResultJson;
            target.CenterResultJson = source.CenterResultJson;
            target.ReceiptKey = source.ReceiptKey;
            target.ReceiptPayloadJson = source.ReceiptPayloadJson;
            target.State = source.State;
            target.ErrorCode = source.ErrorCode;
            target.CorrelationId = source.CorrelationId;
            target.ReviewNote = source.ReviewNote;
        }
    }

    public sealed class MesWorkstationService : IDisposable
    {
        private readonly IMesApiClient _client;
        private readonly MesPendingOperationStore _pendingStore;

        public MesWorkstationService(IMesApiClient client, MesPendingOperationStore pendingStore)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        }

        public MesConnectionOptions Options => _client.Options;
        public IReadOnlyList<MesPendingOperation> PendingOperations => _pendingStore.GetAll();
        public string PendingOperationsError => _pendingStore.LoadError;
        public void Configure(MesConnectionOptions options, string accessToken) => _client.Configure(options, accessToken);

        public Task<MesResult<JsonElement>> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            _client.GetAsync<JsonElement>("/health", cancellationToken);

        public Task<MesResult<MesOrderSnapshot>> GetOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
            _client.GetAsync<MesOrderSnapshot>("/api/orders/" + Escape(orderId), cancellationToken);

        public Task<MesResult<JsonElement>> TransitionOrderAsync(string orderId, MesOrderTransitionRequest request,
            CancellationToken cancellationToken = default) =>
            PostAsync($"/api/orders/{Escape(orderId)}/transitions", request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateProductionUnitAsync(MesProductionUnitRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/production-units", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateRouteAsync(MesRouteRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/routes", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateStationAsync(MesStationRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/stations", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreatePackagingUnitAsync(MesPackagingUnitRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/packaging-units", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ChangeNumberStatusAsync(string allocationId, MesNumberStatusRequest request,
            CancellationToken cancellationToken = default) => PostAsync(
                $"/api/number-allocations/{Escape(allocationId)}/status", request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> GetNumberHistoryAsync(string allocationId,
            CancellationToken cancellationToken = default) =>
            _client.GetAsync<JsonElement>($"/api/number-allocations/{Escape(allocationId)}/history", cancellationToken);

        public Task<MesResult<JsonElement>> CreateWeightRuleAsync(MesWeightRuleRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/weight-rules", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> RecordWeightAsync(string packagingUnitId, MesWeightRequest request,
            CancellationToken cancellationToken = default) => PostAsync(
                $"/api/packaging-units/{Escape(packagingUnitId)}/weights", request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateIdentifierWriteTaskAsync(MesIdentifierWriteTaskRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/identifier-write-tasks", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ClaimIdentifierWriteTaskAsync(string platform, string idempotencyKey,
            CancellationToken cancellationToken = default) => PostAsync("/api/identifier-write-tasks/claims",
                new MesIdentifierWriteClaimRequest { Platform = platform, IdempotencyKey = idempotencyKey },
                idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> RecordIdentifierWriteResultAsync(string taskId,
            MesIdentifierWriteResultRequest request, CancellationToken cancellationToken = default) => PostAsync(
                $"/api/identifier-write-tasks/{Escape(taskId)}/results", request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateInspectionLotAsync(MesInspectionLotRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/inspection-lots", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> GetQualityDispositionTasksAsync(string status,
            CancellationToken cancellationToken = default) => _client.GetAsync<JsonElement>(
                "/api/quality-disposition-tasks?status=" + Escape(status), cancellationToken);

        public Task<MesResult<JsonElement>> AddInspectionResultAsync(string lotId, MesInspectionResultRequest request,
            CancellationToken cancellationToken = default) => PostAsync($"/api/inspection-lots/{Escape(lotId)}/results",
                request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CompleteInspectionLotAsync(string lotId, MesInspectionCompleteRequest request,
            CancellationToken cancellationToken = default) => PostAsync($"/api/inspection-lots/{Escape(lotId)}/complete",
                request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ApplyInspectionDispositionAsync(string lotId,
            MesInspectionDispositionRequest request, CancellationToken cancellationToken = default) => PostAsync(
                $"/api/inspection-lots/{Escape(lotId)}/disposition", request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateReworkOrderAsync(MesReworkOrderRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/rework-orders", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ChangeReworkStateAsync(string reworkOrderId, string command,
            string idempotencyKey, CancellationToken cancellationToken = default) => PostAsync(
                $"/api/rework-orders/{Escape(reworkOrderId)}/{Escape(command)}",
                new MesIdempotentRequest { IdempotencyKey = idempotencyKey }, idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> CreateShipmentAsync(MesShipmentRequest request,
            CancellationToken cancellationToken = default) => PostAsync("/api/shipments", request,
                request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> AddShipmentCartonAsync(string shipmentId, MesShipmentCartonRequest request,
            CancellationToken cancellationToken = default) => PostAsync($"/api/shipments/{Escape(shipmentId)}/cartons",
                request, request.IdempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ConfirmShipmentAsync(string shipmentId, string idempotencyKey,
            CancellationToken cancellationToken = default) => PostAsync($"/api/shipments/{Escape(shipmentId)}/confirm",
                new MesIdempotentRequest { IdempotencyKey = idempotencyKey }, idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> ArchiveOrderAsync(string orderId, string idempotencyKey,
            CancellationToken cancellationToken = default) => PostAsync($"/api/orders/{Escape(orderId)}/archive",
                new MesIdempotentRequest { IdempotencyKey = idempotencyKey }, idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> GetOrderArchiveAsync(string orderId,
            CancellationToken cancellationToken = default) =>
            _client.GetAsync<JsonElement>($"/api/orders/{Escape(orderId)}/archive", cancellationToken);

        public Task<MesResult<JsonElement>> GetArchiveRepairTasksAsync(string status,
            CancellationToken cancellationToken = default) => _client.GetAsync<JsonElement>(
                "/api/archive-repair-tasks?status=" + Escape(status), cancellationToken);

        public Task<MesResult<JsonElement>> RepairArchiveAsync(string repairTaskId, string idempotencyKey,
            CancellationToken cancellationToken = default) => PostAsync(
                $"/api/archive-repair-tasks/{Escape(repairTaskId)}/repair",
                new MesIdempotentRequest { IdempotencyKey = idempotencyKey }, idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> StageCsvImportAsync(string type, byte[] content, string idempotencyKey,
            CancellationToken cancellationToken = default) => _client.PostBytesAsync<JsonElement>(
                $"/api/csv-imports/{Escape(type)}", content, "text/csv", idempotencyKey, cancellationToken);

        public Task<MesResult<JsonElement>> GetCsvImportAsync(string batchId,
            CancellationToken cancellationToken = default) =>
            _client.GetAsync<JsonElement>($"/api/csv-imports/{Escape(batchId)}", cancellationToken);

        public Task<MesResult<JsonElement>> ConfirmCsvImportAsync(string batchId, string idempotencyKey,
            CancellationToken cancellationToken = default) => PostAsync($"/api/csv-imports/{Escape(batchId)}/confirm",
                new MesIdempotentRequest { IdempotencyKey = idempotencyKey }, idempotencyKey, cancellationToken);

        public Task<MesResult<string>> ExportCsvAsync(string type, CancellationToken cancellationToken = default) =>
            _client.GetAsync<string>($"/api/csv-exports/{Escape(type)}", cancellationToken);

        public Task<MesResult<JsonElement>> PassStationAsync(MesStationPassRequest request, CancellationToken cancellationToken = default) =>
            ExecuteOnlineOnlyAsync("StationPass", request.UnitId, request.IdempotencyKey, "/api/station-passes", request, cancellationToken);

        public Task<MesResult<JsonElement>> BindPackagingAsync(MesPackagingBindRequest request, CancellationToken cancellationToken = default) =>
            ExecuteOnlineOnlyAsync("PackagingBinding", request.ParentId, request.IdempotencyKey, "/api/packaging-bindings", request, cancellationToken);

        public async Task<MesResult<MesPrintClaimResult>> ClaimPrintJobAsync(string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var request = new { idempotencyKey };
            var claim = _pendingStore.Upsert(new MesPendingOperation
            {
                Kind = "PrintClaim",
                IdempotencyKey = idempotencyKey,
                RequestPath = "/api/print-jobs/claims",
                RequestJson = JsonSerializer.Serialize(request),
                State = MesPendingState.Pending
            });
            var result = await _client.PostAsync<MesPrintClaimResult>("/api/print-jobs/claims",
                request, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value?.Job != null)
                PreservePrintJob(result.Value.Job, "", MesPendingState.Pending, "");
            _pendingStore.Update(claim.Id, item =>
            {
                item.BusinessId = result.Value?.Job?.JobId ?? "";
                item.CenterResultJson = result.IsSuccess ? JsonSerializer.Serialize(result.Value) : "";
                item.ErrorCode = result.IsSuccess ? "" : result.Error?.Code ?? "MES_UNAVAILABLE";
                item.State = result.IsSuccess ? MesPendingState.Synced : MesPendingState.Pending;
            });
            return result;
        }

        public async Task<MesResult<MesPrintJob>> SubmitPrintReceiptAsync(MesPrintJob job, string state,
            object localResult, CancellationToken cancellationToken = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            var receiptKey = "receipt-" + job.IdempotencyKey;
            var localJson = JsonSerializer.Serialize(localResult ?? new { });
            var receiptPayload = new { idempotencyKey = receiptKey, state, result = localResult ?? new { } };
            PreservePrintJob(job, localJson, MesPendingState.Pending, "", receiptKey,
                JsonSerializer.Serialize(receiptPayload));
            var result = await _client.PostAsync<JsonElement>($"/api/print-jobs/{Escape(job.JobId)}/receipts",
                receiptPayload, receiptKey, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                MarkPrintError(job.IdempotencyKey, result.Error?.Code ?? "MES_UNAVAILABLE");
                return MesResult<MesPrintJob>.Failure(result.Error, result.StatusCode);
            }
            var operation = _pendingStore.GetAll()
                .First(item => item.Kind == "PrintJob" && item.IdempotencyKey == job.IdempotencyKey);
            _pendingStore.Update(operation.Id, item =>
            {
                item.CenterResultJson = JsonSerializer.Serialize(result.Value);
                item.ErrorCode = "";
                item.State = MesPendingState.Synced;
            });
            var refreshed = await GetPrintJobByIdempotencyKeyAsync(job.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (refreshed.IsSuccess) return refreshed;
            job.State = state;
            job.ResultJson = localJson;
            return MesResult<MesPrintJob>.Success(job, result.CorrelationId, result.StatusCode);
        }

        public Task<MesResult<MesPrintJob>> GetPrintJobByIdempotencyKeyAsync(string key,
            CancellationToken cancellationToken = default) =>
            _client.GetAsync<MesPrintJob>("/api/print-jobs/by-idempotency-key/" + Escape(key), cancellationToken);

        public Task<MesResult<JsonElement>> QueryTraceabilityAsync(string type, string value,
            CancellationToken cancellationToken = default) =>
            _client.GetAsync<JsonElement>($"/api/traceability?type={Escape(type)}&value={Escape(value)}", cancellationToken);

        public async Task<MesPrintRecoveryResult> RecoverPrintJobsAsync(CancellationToken cancellationToken = default)
        {
            var recovered = 0;
            foreach (var claim in _pendingStore.GetAll().Where(item => item.Kind == "PrintClaim" && item.State == MesPendingState.Pending))
            {
                var request = ParsePayload(claim.RequestJson);
                var result = await _client.PostAsync<MesPrintClaimResult>(claim.RequestPath, request,
                    claim.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess && result.Value?.Job != null)
                    PreservePrintJob(result.Value.Job, "", MesPendingState.Pending, "");
                _pendingStore.Update(claim.Id, item =>
                {
                    item.BusinessId = result.Value?.Job?.JobId ?? item.BusinessId;
                    item.CenterResultJson = result.IsSuccess ? JsonSerializer.Serialize(result.Value) : "";
                    item.ErrorCode = result.IsSuccess ? "" : result.Error?.Code ?? "MES_UNAVAILABLE";
                    item.State = result.IsSuccess ? MesPendingState.Synced : MesPendingState.Pending;
                });
                if (result.IsSuccess) recovered++;
            }
            foreach (var operation in _pendingStore.GetAll().Where(item => item.Kind == "PrintJob" && item.State == MesPendingState.Pending))
            {
                if (!string.IsNullOrWhiteSpace(operation.ReceiptPayloadJson))
                {
                    var receipt = await _client.PostAsync<JsonElement>(
                        $"/api/print-jobs/{Escape(operation.BusinessId)}/receipts",
                        ParsePayload(operation.ReceiptPayloadJson), operation.ReceiptKey, cancellationToken).ConfigureAwait(false);
                    if (!receipt.IsSuccess)
                    {
                        _pendingStore.Update(operation.Id, item => item.ErrorCode = receipt.Error?.Code ?? "MES_UNAVAILABLE");
                        continue;
                    }
                }
                var result = await GetPrintJobByIdempotencyKeyAsync(operation.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    if (result.StatusCode != (int)HttpStatusCode.NotFound)
                        _pendingStore.Update(operation.Id, item => item.ErrorCode = result.Error?.Code ?? "MES_UNAVAILABLE");
                    continue;
                }
                var centerJson = JsonSerializer.Serialize(result.Value);
                _pendingStore.Update(operation.Id, item =>
                {
                    item.CenterResultJson = centerJson;
                    item.ErrorCode = "";
                    item.State = ResolveRecoveredState(item.LocalResultJson, result.Value);
                });
                recovered++;
            }
            var printableJobs = _pendingStore.GetAll()
                .Where(item => item.Kind == "PrintJob" && item.State == MesPendingState.Pending &&
                    string.IsNullOrWhiteSpace(item.LocalResultJson))
                .Select(item => DeserializePrintJob(item.RequestJson))
                .Where(item => item != null && IsAwaitingLocalPrint(item.State))
                .ToList();
            return new MesPrintRecoveryResult { RecoveredCount = recovered, PrintableJobs = printableJobs };
        }

        public async Task<MesResult<JsonElement>> ResubmitPendingOperationAsync(string operationId,
            CancellationToken cancellationToken = default)
        {
            var operation = _pendingStore.GetAll().FirstOrDefault(item => item.Id == operationId);
            if (operation == null)
                return LocalFailure("PENDING_OPERATION_NOT_FOUND", "待恢复操作不存在。", false);
            if (operation.Kind != "StationPass" && operation.Kind != "PackagingBinding")
                return LocalFailure("PENDING_OPERATION_UNSUPPORTED", "该记录需要使用对应业务恢复流程。", false);
            var result = await _client.PostAsync<JsonElement>(operation.RequestPath, ParsePayload(operation.RequestJson),
                operation.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            UpdatePendingResult(operation.Id, result);
            return result;
        }

        public MesResult<JsonElement> MarkPendingOperationForManualReview(string operationId, string note)
        {
            var operation = _pendingStore.GetAll().FirstOrDefault(item => item.Id == operationId);
            if (operation == null)
                return LocalFailure("PENDING_OPERATION_NOT_FOUND", "待恢复操作不存在。", false);
            _pendingStore.Update(operation.Id, item =>
            {
                item.State = MesPendingState.ReviewRequired;
                item.ReviewNote = note?.Trim() ?? "";
                item.ErrorCode = "MANUAL_REVIEW_REQUIRED";
            });
            return MesResult<JsonElement>.Success(default, "", 200);
        }

        private async Task<MesResult<JsonElement>> ExecuteOnlineOnlyAsync(string kind, string businessId,
            string idempotencyKey, string path, object request, CancellationToken cancellationToken)
        {
            var operation = _pendingStore.Upsert(new MesPendingOperation
            {
                Kind = kind,
                BusinessId = businessId ?? "",
                IdempotencyKey = idempotencyKey ?? "",
                RequestJson = JsonSerializer.Serialize(request),
                RequestPath = path,
                State = MesPendingState.Pending
            });
            var result = await _client.PostAsync<JsonElement>(path, request, idempotencyKey, cancellationToken).ConfigureAwait(false);
            _pendingStore.Update(operation.Id, item =>
            {
                item.ErrorCode = result.IsSuccess ? "" : result.Error?.Code ?? "MES_UNAVAILABLE";
                item.CorrelationId = result.CorrelationId;
                item.CenterResultJson = result.IsSuccess ? JsonSerializer.Serialize(result.Value) : "";
                item.State = result.IsSuccess ? MesPendingState.Synced : MesPendingState.Pending;
            });
            if (!result.IsSuccess && (result.Error?.Code == "MES_UNAVAILABLE" || result.Error?.Code == "MES_TIMEOUT"))
                result.Error = new MesApiError
                {
                    Code = "ONLINE_VALIDATION_REQUIRED",
                    Message = "该操作需要 MES 在线校验，业务意图已在本机保留。",
                    CorrelationId = result.CorrelationId,
                    Retryable = true
                };
            return result;
        }

        private Task<MesResult<JsonElement>> PostAsync(string path, object request, string idempotencyKey,
            CancellationToken cancellationToken) =>
            _client.PostAsync<JsonElement>(path, request, idempotencyKey, cancellationToken);

        private void UpdatePendingResult(string operationId, MesResult<JsonElement> result)
        {
            _pendingStore.Update(operationId, item =>
            {
                item.ErrorCode = result.IsSuccess ? "" : result.Error?.Code ?? "MES_UNAVAILABLE";
                item.CorrelationId = result.CorrelationId;
                item.CenterResultJson = result.IsSuccess ? JsonSerializer.Serialize(result.Value) : "";
                item.State = result.IsSuccess ? MesPendingState.Synced : MesPendingState.Pending;
            });
        }

        private static MesResult<JsonElement> LocalFailure(string code, string message, bool retryable) =>
            MesResult<JsonElement>.Failure(new MesApiError { Code = code, Message = message, Retryable = retryable });

        private void PreservePrintJob(MesPrintJob job, string localResultJson, MesPendingState state, string errorCode,
            string receiptKey = "", string receiptPayloadJson = "")
        {
            _pendingStore.Upsert(new MesPendingOperation
            {
                Kind = "PrintJob",
                BusinessId = job.JobId,
                IdempotencyKey = job.IdempotencyKey,
                RequestJson = JsonSerializer.Serialize(job),
                LocalResultJson = localResultJson,
                ReceiptKey = receiptKey,
                ReceiptPayloadJson = receiptPayloadJson,
                State = state,
                ErrorCode = errorCode
            });
        }

        private void MarkPrintError(string key, string code)
        {
            var operation = _pendingStore.GetAll().FirstOrDefault(item => item.Kind == "PrintJob" && item.IdempotencyKey == key);
            if (operation != null) _pendingStore.Update(operation.Id, item => item.ErrorCode = code);
        }

        private static bool ResultsConflict(string localResultJson, MesPrintJob center)
        {
            if (string.IsNullOrWhiteSpace(localResultJson) || center == null) return false;
            try
            {
                using var local = JsonDocument.Parse(localResultJson);
                var localState = local.RootElement.TryGetProperty("state", out var state) ? state.GetString() : "";
                return !string.IsNullOrWhiteSpace(localState) && !string.Equals(localState, center.State, StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException) { return true; }
        }

        private static MesPendingState ResolveRecoveredState(string localResultJson, MesPrintJob center)
        {
            if (string.IsNullOrWhiteSpace(localResultJson))
                return IsAwaitingLocalPrint(center?.State) ? MesPendingState.Pending : MesPendingState.ReviewRequired;
            return ResultsConflict(localResultJson, center) ? MesPendingState.ReviewRequired : MesPendingState.Synced;
        }

        private static bool IsAwaitingLocalPrint(string state) =>
            string.Equals(state, "Pending", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Received", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Claimed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Submitting", StringComparison.OrdinalIgnoreCase);

        private static MesPrintJob DeserializePrintJob(string json)
        {
            try { return JsonSerializer.Deserialize<MesPrintJob>(json); }
            catch (JsonException) { return null; }
        }

        private static JsonElement ParsePayload(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static string Escape(string value) => Uri.EscapeDataString(value?.Trim() ?? "");
        public void Dispose() => _client.Dispose();
    }
}

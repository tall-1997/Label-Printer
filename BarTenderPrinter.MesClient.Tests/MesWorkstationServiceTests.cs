using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BarTenderPrinter.Tests;

public sealed class MesWorkstationServiceTests
{
    [Fact]
    public async Task ApiClient_RetriesTransientResponse_WithStableCorrelationAndIdempotencyKeys()
    {
        var handler = new RecordingHandler((request, attempt) => attempt == 1
            ? Json(HttpStatusCode.ServiceUnavailable, new { code = "BUSY", message = "busy", correlationId = "server-1", retryable = true })
            : Json(HttpStatusCode.OK, new { id = "pass-1" }));
        using var client = CreateClient(handler, retries: 2);

        var result = await client.PostAsync<JsonElement>("/api/station-passes", new { unitId = "unit-1" }, "idem-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("idem-1", request.IdempotencyKey));
        Assert.Single(handler.Requests.Select(request => request.CorrelationId).Distinct());
    }

    [Fact]
    public async Task ApiClient_DoesNotRetryBusinessConflict()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.Conflict,
            new { code = "IDEMPOTENCY_CONFLICT", message = "conflict", correlationId = "server-2", retryable = false }));
        using var client = CreateClient(handler, retries: 3);

        var result = await client.PostAsync<JsonElement>("/api/station-passes", new { }, "idem-2");

        Assert.False(result.IsSuccess);
        Assert.Equal("IDEMPOTENCY_CONFLICT", result.Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ApiClient_LogsRedactedPathAndNeverLogsTokenOrQueryValue()
    {
        var log = new CapturingLog();
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { found = true }));
        using var client = CreateClient(handler, retries: 0, log: log, token: "secret-token");

        await client.GetAsync<JsonElement>("/api/traceability?type=Imei&value=867530900000001");

        var text = string.Join("\n", log.Messages);
        Assert.DoesNotContain("secret-token", text);
        Assert.DoesNotContain("867530900000001", text);
        Assert.Contains("?<redacted>", text);
    }

    [Fact]
    public async Task StationPass_DisconnectionPreservesIntentAndRequiresOnlineValidation()
    {
        using var fixture = new ServiceFixture(new RecordingHandler((_, _) => throw new HttpRequestException("offline")));
        var request = new MesStationPassRequest
        {
            UnitId = "unit-1", OrderId = "order-1", RouteId = "route-1", OperationId = "op-1", IdempotencyKey = "pass-key"
        };

        var result = await fixture.Service.PassStationAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("ONLINE_VALIDATION_REQUIRED", result.Error.Code);
        var pending = Assert.Single(fixture.Service.PendingOperations);
        Assert.Equal("pass-key", pending.IdempotencyKey);
        Assert.Equal(MesPendingState.Pending, pending.State);
    }

    [Fact]
    public async Task RecoveryQueriesCenterByOriginalIdempotencyKeyAndMarksSynced()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.EndsWith("/api/print-jobs/by-idempotency-key/print-key", request.Path);
            return Json(HttpStatusCode.OK, PrintJob("Submitted"));
        });
        using var fixture = new ServiceFixture(handler);
        fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "PrintJob", BusinessId = "job-1", IdempotencyKey = "print-key",
            LocalResultJson = JsonSerializer.Serialize(new { state = "Submitted" }), State = MesPendingState.Pending
        });

        var recovered = await fixture.Service.RecoverPrintJobsAsync();

        Assert.Equal(1, recovered.RecoveredCount);
        Assert.Equal(MesPendingState.Synced, Assert.Single(fixture.Service.PendingOperations).State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RecoveryPreservesBothSnapshotsWhenCenterConflictsWithLocalResult()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, PrintJob("Failed")));
        using var fixture = new ServiceFixture(handler);
        fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "PrintJob", BusinessId = "job-1", IdempotencyKey = "print-key",
            LocalResultJson = JsonSerializer.Serialize(new { state = "Submitted" }), State = MesPendingState.Pending
        });

        await fixture.Service.RecoverPrintJobsAsync();

        var operation = Assert.Single(fixture.Service.PendingOperations);
        Assert.Equal(MesPendingState.ReviewRequired, operation.State);
        Assert.Contains("Submitted", operation.LocalResultJson);
        Assert.Contains("Failed", operation.CenterResultJson);
    }

    [Fact]
    public async Task Claim_ResponseLost_PersistsIntentAndRecoversWithOriginalKey()
    {
        var handler = new RecordingHandler((request, attempt) =>
        {
            if (attempt == 1) throw new HttpRequestException("response lost");
            return request.Path.Contains("/claims", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, new { job = PrintJob("Submitting"), isReplay = true })
                : Json(HttpStatusCode.OK, PrintJob("Submitting"));
        });
        using var fixture = new ServiceFixture(handler);

        var first = await fixture.Service.ClaimPrintJobAsync("claim-stable-key");
        var recovered = await fixture.Service.RecoverPrintJobsAsync();

        Assert.False(first.IsSuccess);
        Assert.Equal(2, recovered.RecoveredCount);
        Assert.Equal("job-1", Assert.Single(recovered.PrintableJobs).JobId);
        Assert.All(handler.Requests.Take(2), request => Assert.Equal("claim-stable-key", request.IdempotencyKey));
        var operations = fixture.Service.PendingOperations;
        Assert.Equal(MesPendingState.Synced, Assert.Single(operations, item => item.Kind == "PrintClaim").State);
        Assert.Equal(MesPendingState.Pending, Assert.Single(operations, item => item.Kind == "PrintJob").State);
    }

    [Fact]
    public async Task Receipt_Failure_PersistsKeyAndPayloadAndResendsBeforeQuery()
    {
        var handler = new RecordingHandler((request, attempt) =>
        {
            if (attempt == 1) throw new HttpRequestException("offline");
            if (request.Path.Contains("/receipts", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { accepted = true });
            return Json(HttpStatusCode.OK, PrintJob("Submitted"));
        });
        using var fixture = new ServiceFixture(handler);
        var job = PrintJob("Claimed");

        var first = await fixture.Service.SubmitPrintReceiptAsync(job, "Submitted", new { state = "Submitted", copies = 1 });
        var pending = Assert.Single(fixture.Service.PendingOperations);
        var recovered = await fixture.Service.RecoverPrintJobsAsync();

        Assert.False(first.IsSuccess);
        Assert.Equal("receipt-print-key", pending.ReceiptKey);
        Assert.Contains("copies", pending.ReceiptPayloadJson);
        Assert.Equal(1, recovered.RecoveredCount);
        Assert.Equal("receipt-print-key", handler.Requests[1].IdempotencyKey);
        Assert.Contains("copies", handler.Requests[1].Body);
        Assert.Equal(MesPendingState.Synced, Assert.Single(fixture.Service.PendingOperations).State);
    }

    [Fact]
    public async Task Recovery_ClaimedWithoutLocalPrint_RemainsPending()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, PrintJob("Claimed")));
        using var fixture = new ServiceFixture(handler);
        fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "PrintJob", BusinessId = "job-1", IdempotencyKey = "print-key", State = MesPendingState.Pending
        });

        await fixture.Service.RecoverPrintJobsAsync();

        Assert.Equal(MesPendingState.Pending, Assert.Single(fixture.Service.PendingOperations).State);
    }

    [Fact]
    public async Task Recovery_SubmittingWithoutLocalPrint_ReturnsPrintableJobAndRemainsPending()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, PrintJob("Submitting")));
        using var fixture = new ServiceFixture(handler);
        fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "PrintJob", BusinessId = "job-1", IdempotencyKey = "print-key",
            RequestJson = JsonSerializer.Serialize(PrintJob("Submitting")), State = MesPendingState.Pending
        });

        var recovery = await fixture.Service.RecoverPrintJobsAsync();

        Assert.Equal(MesPendingState.Pending, Assert.Single(fixture.Service.PendingOperations).State);
        Assert.Equal("job-1", Assert.Single(recovery.PrintableJobs).JobId);
    }

    [Fact]
    public async Task Receipt_FirstSuccessImmediatelyMarksLocalOperationSynced()
    {
        var handler = new RecordingHandler((request, _) => request.Path.Contains("/receipts", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, new { accepted = true })
            : throw new HttpRequestException("query unavailable"));
        using var fixture = new ServiceFixture(handler);

        var result = await fixture.Service.SubmitPrintReceiptAsync(PrintJob("Submitting"), "Submitted", new { state = "Submitted" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Submitted", result.Value.State);
        Assert.Equal(MesPendingState.Synced, Assert.Single(fixture.Service.PendingOperations).State);
    }

    [Fact]
    public void ApiClient_RejectsBearerTokenOverRemoteHttp_ButAllowsLoopback()
    {
        using var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { }));
        Assert.Throws<ArgumentException>(() => new MesApiClient(new MesConnectionOptions
        {
            BaseUrl = "http://mes.example.test"
        }, "token", handler: handler));

        using var loopback = new MesApiClient(new MesConnectionOptions { BaseUrl = "http://127.0.0.1:5000" },
            "token", handler: new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { })));
    }

    [Fact]
    public async Task ApiClient_UsesImmutableConfigurationSnapshot()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { ok = true }));
        var options = new MesConnectionOptions { BaseUrl = "https://mes.example.test", TimeoutSeconds = 2, MaxRetries = 0 };
        using var client = new MesApiClient(options, "token", handler: handler);
        options.BaseUrl = "http://changed.example.test";
        client.Options.BaseUrl = "http://also-changed.example.test";

        await client.GetAsync<JsonElement>("/health");

        Assert.Equal("mes.example.test", handler.Requests.Single().Host);
    }

    [Fact]
    public void PendingStore_CorruptFile_LoadsAsAuditableErrorAndBlocksOverwrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mes-client-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pending.json");
        File.WriteAllText(path, "{corrupt");
        var store = new MesPendingOperationStore(path);

        Assert.Empty(store.GetAll());
        Assert.Contains("MES_PENDING_STORE_CORRUPT", store.LoadError);
        Assert.Contains(path, store.LoadError);
        Assert.Throws<JsonException>(() => store.Upsert(new MesPendingOperation { Kind = "PrintJob", IdempotencyKey = "key" }));
        Assert.Equal("{corrupt", File.ReadAllText(path));
        Directory.Delete(directory, true);
    }

    [Fact]
    public void ApiClient_DisposesInjectedHandler()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { }));
        var client = CreateClient(handler, retries: 0);

        client.Dispose();

        Assert.True(handler.IsDisposed);
    }

    [Theory]
    [InlineData("fieldValues")]
    [InlineData("fields")]
    public void PrintRequestSnapshot_AcceptsBothFieldShapesWithoutUiFallback(string propertyName)
    {
        var json = $$"""
            {
              "templatePath": "/immutable/template.btw",
              "printer": "MES-Printer",
              "{{propertyName}}": { "SerialNumber": "SN-1", "Quantity": 2 }
            }
            """;

        var snapshot = MesPrintRequestSnapshot.Parse(json);

        Assert.Equal("/immutable/template.btw", snapshot.TemplatePath);
        Assert.Equal("MES-Printer", snapshot.Printer);
        Assert.Equal("SN-1", snapshot.Fields["SerialNumber"]);
        Assert.Equal("2", snapshot.Fields["Quantity"]);
    }

    [Fact]
    public void PrintRequestSnapshot_MissingImmutableFields_RemainsExplicitlyMissing()
    {
        var snapshot = MesPrintRequestSnapshot.Parse("{\"fields\":{\"SerialNumber\":\"SN-1\"}}");

        Assert.Empty(snapshot.TemplatePath);
        Assert.Empty(snapshot.Printer);
        Assert.Single(snapshot.Fields);
    }

    [Theory]
    [InlineData("order-transition", "/api/orders/order%201/transitions")]
    [InlineData("number-status", "/api/number-allocations/allocation%201/status")]
    [InlineData("weight", "/api/packaging-units/carton%201/weights")]
    [InlineData("write-result", "/api/identifier-write-tasks/task%201/results")]
    [InlineData("quality", "/api/inspection-lots/lot%201/results")]
    [InlineData("rework", "/api/rework-orders/rework%201/activate")]
    [InlineData("shipping", "/api/shipments/shipment%201/cartons")]
    [InlineData("archive", "/api/orders/order%201/archive")]
    [InlineData("archive-repair", "/api/archive-repair-tasks/repair%201/repair")]
    public async Task ExtendedCommands_UseEscapedResourceRoutesAndIdempotencyKey(string command, string expectedPath)
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { accepted = true }));
        using var fixture = new ServiceFixture(handler);

        switch (command)
        {
            case "order-transition":
                await fixture.Service.TransitionOrderAsync("order 1", new MesOrderTransitionRequest { IdempotencyKey = "key-1" });
                break;
            case "number-status":
                await fixture.Service.ChangeNumberStatusAsync("allocation 1", new MesNumberStatusRequest { IdempotencyKey = "key-1" });
                break;
            case "weight":
                await fixture.Service.RecordWeightAsync("carton 1", new MesWeightRequest { IdempotencyKey = "key-1" });
                break;
            case "write-result":
                await fixture.Service.RecordIdentifierWriteResultAsync("task 1", new MesIdentifierWriteResultRequest
                {
                    IdempotencyKey = "key-1", Result = EmptyObject()
                });
                break;
            case "quality":
                await fixture.Service.AddInspectionResultAsync("lot 1", new MesInspectionResultRequest { IdempotencyKey = "key-1" });
                break;
            case "rework":
                await fixture.Service.ChangeReworkStateAsync("rework 1", "activate", "key-1");
                break;
            case "shipping":
                await fixture.Service.AddShipmentCartonAsync("shipment 1", new MesShipmentCartonRequest { IdempotencyKey = "key-1" });
                break;
            case "archive":
                await fixture.Service.ArchiveOrderAsync("order 1", "key-1");
                break;
            case "archive-repair":
                await fixture.Service.RepairArchiveAsync("repair 1", "key-1");
                break;
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expectedPath, request.Path);
        Assert.Equal("key-1", request.IdempotencyKey);
    }

    [Fact]
    public async Task DataExchange_UsesBatchAndJobEndpoints()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { accepted = true }));
        using var fixture = new ServiceFixture(handler);

        await fixture.Service.StageCsvImportAsync("orders", Encoding.UTF8.GetBytes("orderNumber\r\nO-1"), "import-key");
        await fixture.Service.GetCsvImportAsync("batch 1");
        await fixture.Service.ConfirmCsvImportAsync("batch 1", "confirm-key");
        await fixture.Service.ExportCsvAsync("orders");

        Assert.Equal(new[]
        {
            "/api/csv-imports/orders", "/api/csv-imports/batch%201",
            "/api/csv-imports/batch%201/confirm", "/api/csv-exports/orders"
        }, handler.Requests.Select(request => request.Path));
        Assert.Equal("import-key", handler.Requests[0].IdempotencyKey);
        Assert.Equal("orderNumber\r\nO-1", handler.Requests[0].Body);
    }

    [Theory]
    [InlineData("StationPass", "/api/station-passes")]
    [InlineData("PackagingBinding", "/api/packaging-bindings")]
    public async Task PendingBusinessOperation_CanBeResubmittedWithOriginalPayloadAndKey(string kind, string path)
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { accepted = true }));
        using var fixture = new ServiceFixture(handler);
        var operation = fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = kind, BusinessId = "business-1", IdempotencyKey = "original-key",
            RequestPath = path, RequestJson = "{\"value\":\"original\"}", State = MesPendingState.Pending
        });

        var result = await fixture.Service.ResubmitPendingOperationAsync(operation.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("original-key", Assert.Single(handler.Requests).IdempotencyKey);
        Assert.Contains("original", handler.Requests[0].Body);
        Assert.Equal(MesPendingState.Synced, Assert.Single(fixture.Service.PendingOperations).State);
    }

    [Fact]
    public void PendingBusinessOperation_CanBeMarkedForManualReview()
    {
        using var fixture = new ServiceFixture(new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new { })));
        var operation = fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "StationPass", IdempotencyKey = "key", RequestJson = "{}", State = MesPendingState.Pending
        });

        var result = fixture.Service.MarkPendingOperationForManualReview(operation.Id, "physical unit verified");

        Assert.True(result.IsSuccess);
        var pending = Assert.Single(fixture.Service.PendingOperations);
        Assert.Equal(MesPendingState.ReviewRequired, pending.State);
        Assert.Equal("MANUAL_REVIEW_REQUIRED", pending.ErrorCode);
        Assert.Equal("physical unit verified", pending.ReviewNote);
    }

    [Fact]
    public async Task FailedPendingResubmission_PreservesCorrelationIdForRecoveryUi()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.Conflict,
            new { code = "VERSION_CONFLICT", message = "conflict", correlationId = "center-correlation", retryable = false }));
        using var fixture = new ServiceFixture(handler);
        var operation = fixture.Store.Upsert(new MesPendingOperation
        {
            Kind = "PackagingBinding", IdempotencyKey = "key", RequestPath = "/api/packaging-bindings",
            RequestJson = "{}", State = MesPendingState.Pending
        });

        await fixture.Service.ResubmitPendingOperationAsync(operation.Id);

        var pending = Assert.Single(fixture.Service.PendingOperations);
        Assert.Equal("VERSION_CONFLICT", pending.ErrorCode);
        Assert.Equal("center-correlation", pending.CorrelationId);
    }

    private static MesPrintJob PrintJob(string state) => new MesPrintJob
    {
        JobId = "job-1", IdempotencyKey = "print-key", LabelType = "Carton", State = state,
        RequestJson = "{}", UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static MesApiClient CreateClient(HttpMessageHandler handler, int retries, CapturingLog log = null,
        string token = "token") => new MesApiClient(new MesConnectionOptions
    {
        BaseUrl = "https://mes.example.test", TimeoutSeconds = 2, MaxRetries = retries
    }, token, log, handler);

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new HttpResponseMessage(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private sealed class ServiceFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mes-client-tests-" + Guid.NewGuid().ToString("N"));
        public MesPendingOperationStore Store { get; }
        public MesWorkstationService Service { get; }

        public ServiceFixture(HttpMessageHandler handler)
        {
            Store = new MesPendingOperationStore(Path.Combine(_directory, "pending.json"));
            Service = new MesWorkstationService(CreateClient(handler, 0), Store);
        }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(_directory, true); } catch { }
        }
    }

    private sealed class CapturingLog : IMesClientLog
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, int, HttpResponseMessage> _response;
        public List<RecordedRequest> Requests { get; } = new();
        public bool IsDisposed { get; private set; }

        public RecordingHandler(Func<RecordedRequest, int, HttpResponseMessage> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new RecordedRequest(
                request.RequestUri!.PathAndQuery,
                request.RequestUri.Host,
                request.Headers.TryGetValues("X-Correlation-ID", out var correlations) ? correlations.Single() : "",
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : "",
                body);
            Requests.Add(recorded);
            return _response(recorded, Requests.Count);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record RecordedRequest(string Path, string Host, string CorrelationId, string IdempotencyKey, string Body);
}

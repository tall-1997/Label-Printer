using BarTenderPrinter.StationAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(
    builder.Configuration.GetValue("StationAgent:Port", 5188)));
builder.Services.AddSingleton<StationOperationStore>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
    {
        var code = exception.Message is "IDEMPOTENCY_CONFLICT" or "CONCURRENCY_CONFLICT" ? exception.Message : "VALIDATION_FAILED";
        context.Response.StatusCode = code.EndsWith("CONFLICT", StringComparison.Ordinal) ? 409 : exception is KeyNotFoundException ? 404 : 400;
        await context.Response.WriteAsJsonAsync(new { code, message = exception.Message });
    }
});
app.MapGet("/agent/v1/health", (IConfiguration configuration) =>
    Results.Ok(AgentCapabilities.Create(configuration, DateTimeOffset.UtcNow)));
app.MapGet("/agent/v1/capabilities", (IConfiguration configuration) =>
    Results.Ok(AgentCapabilities.Create(configuration, DateTimeOffset.UtcNow).Capabilities));
app.MapPost("/agent/v1/operations", (RegisterOperationRequest request, StationOperationStore store) =>
    Results.Ok(store.Register(request)));
app.MapGet("/agent/v1/operations/{id}", (string id, StationOperationStore store) =>
    store.Get(id) is { } operation ? Results.Ok(operation) : Results.NotFound());
app.MapGet("/agent/v1/operations", (StationOperationState? state, int? limit, StationOperationStore store) =>
    Results.Ok(store.List(state, limit ?? 50)));
app.MapPost("/agent/v1/operations/{id}/execute", (string id, long expectedVersion, StationOperationStore store) =>
    Results.Ok(store.Start(id, expectedVersion)));
app.MapPost("/agent/v1/operations/{id}/complete", (string id, CompleteOperationRequest request, StationOperationStore store) =>
    Results.Ok(store.Complete(id, request)));
app.MapPost("/agent/v1/operations/{id}/resolve", (string id, ResolveOperationRequest request, StationOperationStore store) =>
    Results.Ok(store.Resolve(id, request)));
app.MapGet("/agent/v1/outbox", (int? limit, StationOperationStore store) => Results.Ok(store.ListOutbox(limit ?? 50)));
app.MapPost("/agent/v1/outbox/{id}/complete", (string id, long expectedVersion, StationOperationStore store) =>
    Results.Ok(store.UpdateOutbox(id, expectedVersion, true)));
app.MapPost("/agent/v1/outbox/{id}/retry", (string id, long expectedVersion, StationOperationStore store) =>
    Results.Ok(store.UpdateOutbox(id, expectedVersion, false)));
app.Run();

public partial class Program;

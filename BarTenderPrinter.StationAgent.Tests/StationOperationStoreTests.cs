using System.Text.Json;
using BarTenderPrinter.StationAgent;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarTenderPrinter.StationAgent.Tests;

public sealed class StationOperationStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"station-agent-{Guid.NewGuid():N}.db");

    [Fact]
    public void OperationLifecycleCreatesOutboxAtomically()
    {
        var store = CreateStore();
        var operation = store.Register(Request("key-1", "{\"printer\":\"P1\",\"copies\":1}"));
        var executing = store.Start(operation.Id, operation.Version);
        using var result = JsonDocument.Parse("{\"submitted\":true}");

        var completed = store.Complete(operation.Id, new(executing.Version, StationOperationState.Succeeded,
            result.RootElement, "operator-1", "PRINT_CONFIRMED", "打印完成"));

        Assert.Equal(StationOperationState.Succeeded, completed.State);
        Assert.Single(store.ListOutbox(10));
    }

    [Fact]
    public void EquivalentPayloadReplaysAndChangedPayloadConflicts()
    {
        var store = CreateStore();
        var first = store.Register(Request("key-2", "{\"b\":2,\"a\":1}"));
        var replay = store.Register(Request("key-2", "{\"a\":1,\"b\":2}"));

        Assert.Equal(first.Id, replay.Id);
        Assert.Throws<InvalidOperationException>(() => store.Register(Request("key-2", "{\"a\":2,\"b\":2}")));
    }

    [Fact]
    public void StaleVersionCannotStartOperation()
    {
        var store = CreateStore();
        var operation = store.Register(Request("key-3", "{\"job\":1}"));
        store.Start(operation.Id, operation.Version);

        Assert.Throws<InvalidOperationException>(() => store.Start(operation.Id, operation.Version));
    }

    private StationOperationStore CreateStore() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["StationAgent:DatabasePath"] = _path }).Build());

    private static RegisterOperationRequest Request(string key, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new("Print", "job-1", key, document.RootElement.Clone());
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

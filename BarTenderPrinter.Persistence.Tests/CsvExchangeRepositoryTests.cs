using System.Text;
using BarTenderPrinter.Domain.Common;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.Persistence.Tests;

public sealed class CsvExchangeRepositoryTests
{
    [PostgresFact]
    public async Task OrderCsvStagesRowErrorsAndValidBatchCommitsOnce()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
        await new PostgresMigrator(dataSource).MigrateAsync();
        var repository = new CsvExchangeRepository(dataSource);
        var invalidOrderNumber = Unique("BAD-ORDER");
        var invalidSource = Encoding.UTF8.GetBytes($"""
            orderNumber,customer,productModel,color,plannedQuantity,validFromUtc,validToUtc
            {invalidOrderNumber},Customer,M1,BLACK,0,,
            """);

        var invalid = await repository.StageAsync("orders", invalidSource, "planner",
            new IdempotencyKey(Unique("stage")), "invalid-hash", DateTimeOffset.UtcNow);

        Assert.Equal("Invalid", invalid.Status);
        Assert.Single(invalid.Errors);
        var orderNumber = Unique("CSV-ORDER");
        var validSource = Encoding.UTF8.GetBytes($"""
            orderNumber,customer,productModel,color,plannedQuantity,validFromUtc,validToUtc
            {orderNumber},Customer,M1,BLACK,1,,
            """);
        var batch = await repository.StageAsync("orders", validSource, "planner",
            new IdempotencyKey(Unique("stage")), "valid-hash", DateTimeOffset.UtcNow);
        var key = new IdempotencyKey(Unique("confirm"));
        var committed = await repository.ConfirmAsync(batch.Id, key, "confirm-hash", DateTimeOffset.UtcNow);
        var replay = await repository.ConfirmAsync(batch.Id, key, "confirm-hash", DateTimeOffset.UtcNow);

        await using var count = dataSource.CreateCommand("SELECT count(*) FROM production_orders WHERE order_number=$1");
        count.Parameters.AddWithValue(orderNumber);
        Assert.Equal("Committed", committed.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task CsvExportsEscapeFormulaValuesAndMaskSensitiveColumns()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
        await new PostgresMigrator(dataSource).MigrateAsync();
        var repository = new CsvExchangeRepository(dataSource);
        var source = Encoding.UTF8.GetBytes($"""
            orderNumber,customer,productModel,color,plannedQuantity,validFromUtc,validToUtc
            {Unique("EXPORT")},=sensitive,M1,BLACK,1,,
            """);
        var batch = await repository.StageAsync("orders", source, "planner", new IdempotencyKey(Unique("stage")),
            "export-hash", DateTimeOffset.UtcNow);
        await repository.ConfirmAsync(batch.Id, new IdempotencyKey(Unique("confirm")), "confirm-hash",
            DateTimeOffset.UtcNow);

        var masked = await repository.ExportOrdersAsync(false);
        var privileged = await repository.ExportOrdersAsync(true);

        Assert.Contains("\"***\"", masked);
        Assert.Contains("\"'=sensitive\"", privileged);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}

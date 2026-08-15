using Npgsql;

namespace BarTenderPrinter.Persistence;

public sealed record PostgresOptions
{
    public required string ConnectionString { get; init; }

    public NpgsqlDataSource CreateDataSource()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("PostgreSQL 连接字符串不能为空。");
        return NpgsqlDataSource.Create(ConnectionString);
    }
}

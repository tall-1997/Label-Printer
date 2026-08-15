namespace BarTenderPrinter.Persistence;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed class PersistenceConcurrencyException(string entityType, string entityId)
    : Exception($"{entityType} {entityId} 的版本已变化。")
{
    public string EntityType { get; } = entityType;
    public string EntityId { get; } = entityId;
}

public sealed class PersistenceBusinessException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

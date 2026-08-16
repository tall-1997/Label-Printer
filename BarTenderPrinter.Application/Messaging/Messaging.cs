using BarTenderPrinter.Application.Auditing;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Application.Messaging;

public interface ICommand<TResult>;
public interface IQuery<TResult>;

public sealed record CommandContext(IdempotencyKey IdempotencyKey, AuditActor Actor, string CorrelationId);

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CommandContext context, CancellationToken cancellationToken = default);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

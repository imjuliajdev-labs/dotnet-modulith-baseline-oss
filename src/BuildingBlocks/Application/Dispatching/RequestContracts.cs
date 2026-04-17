using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;

namespace BuildingBlocks.Application.Dispatching;

public interface ICommand
{
}

public interface ICommand<TResponse>
{
}

public interface IQuery<TResponse>
{
}

public interface IIdempotencyRequest
{
    string RequestKey { get; }
}

public interface IIdempotentCommand : ICommand, IIdempotencyRequest
{
}

public interface IIdempotentCommand<TResponse> : ICommand<TResponse>, IIdempotencyRequest
{
}

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}

public interface IRequestPipelineBehavior<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

public readonly record struct CommandTransactionContext(Type CommandType, string? ModuleKey);

public interface ICommandTransactionScopeFactory
{
    ValueTask<ICommandTransactionScope> BeginAsync(CommandTransactionContext context, CancellationToken cancellationToken);
}

public interface ICommandTransactionScope : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken cancellationToken);

    ValueTask RollbackAsync(CancellationToken cancellationToken);
}

public interface IRequestValidator<in TRequest>
{
    Task<IReadOnlyList<Error>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TException : Exception
{
    Task<TResponse> Handle(TRequest request, TException exception, CancellationToken cancellationToken);
}

public interface ITimeoutRequest
{
    TimeSpan Timeout { get; }
}

public interface IRequestExceptionAction<in TRequest, in TException>
    where TException : Exception
{
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}

public interface IExceptionToErrorMapper
{
    Error Map(Exception exception);
}

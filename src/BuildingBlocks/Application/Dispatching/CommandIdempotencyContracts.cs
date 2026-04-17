using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching;

public readonly record struct CommandIdempotencyContext(
    Type CommandType,
    string RequestKey,
    string RequestHash,
    string? ModuleKey,
    string? CallerIdentity);

public enum CommandIdempotencyAcquireStatus
{
    Acquired = 0,
    Replay = 1,
    Reject = 2
}

public sealed class CommandIdempotencyAcquireResult
{
    private CommandIdempotencyAcquireResult(
        CommandIdempotencyAcquireStatus status,
        ICommandIdempotencyExecution? execution,
        object? response,
        Error error)
    {
        Status = status;
        Execution = execution;
        Response = response;
        Error = error;
    }

    public CommandIdempotencyAcquireStatus Status { get; }

    public ICommandIdempotencyExecution? Execution { get; }

    public object? Response { get; }

    public Error Error { get; }

    public static CommandIdempotencyAcquireResult Acquired(ICommandIdempotencyExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return new CommandIdempotencyAcquireResult(CommandIdempotencyAcquireStatus.Acquired, execution, response: null, Error.None);
    }

    public static CommandIdempotencyAcquireResult Replay(object response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new CommandIdempotencyAcquireResult(CommandIdempotencyAcquireStatus.Replay, execution: null, response, Error.None);
    }

    public static CommandIdempotencyAcquireResult Reject(Error error)
    {
        if (error == Error.None)
        {
            throw new ArgumentException("A rejected idempotency request requires a concrete error.", nameof(error));
        }

        return new CommandIdempotencyAcquireResult(CommandIdempotencyAcquireStatus.Reject, execution: null, response: null, error);
    }
}

public interface ICommandIdempotencyExecution : IAsyncDisposable
{
    ValueTask CompleteAsync(object response, CancellationToken cancellationToken);

    ValueTask AbandonAsync(CancellationToken cancellationToken);
}

public interface ICommandIdempotencyStore
{
    ValueTask<CommandIdempotencyAcquireResult> BeginAsync(
        Type responseType,
        CommandIdempotencyContext context,
        CancellationToken cancellationToken);
}

public interface ICommandIdempotencyRequestHasher
{
    string ComputeHash<TRequest>(TRequest request);
}

public static class CommandIdempotencyErrors
{
    public static Error RequestKeyRequired(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        return new Error(
            "idempotency.request_key_required",
            $"Command '{commandType.Name}' must provide a non-empty request key.",
            ErrorKind.Validation);
    }

    public static Error RequestConflict(Type commandType, string requestKey)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);

        return new Error(
            "idempotency.request_conflict",
            $"Request key '{requestKey}' was already used for a different payload on command '{commandType.Name}'.",
            ErrorKind.Conflict);
    }

    public static Error RequestInFlight(Type commandType, string requestKey)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);

        return new Error(
            "idempotency.request_in_flight",
            $"Request key '{requestKey}' is already in flight for command '{commandType.Name}'.",
            ErrorKind.Conflict);
    }
}

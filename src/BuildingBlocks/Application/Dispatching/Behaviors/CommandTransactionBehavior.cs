using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class CommandTransactionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly ICommandTransactionScopeFactory _transactionScopeFactory;

    public CommandTransactionBehavior(ICommandTransactionScopeFactory transactionScopeFactory)
    {
        _transactionScopeFactory = transactionScopeFactory ?? throw new ArgumentNullException(nameof(transactionScopeFactory));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!CommandTransactionRuntime.IsCommandRequestType(typeof(TRequest)))
        {
            return await next(cancellationToken);
        }

        await using var transaction = await _transactionScopeFactory.BeginAsync(
            CommandTransactionRuntime.CreateContext(request),
            cancellationToken);

        try
        {
            var response = await next(cancellationToken);

            // Once a command enters the transaction boundary, finalization must not be aborted by request cancellation.
            if (CommandTransactionRuntime.IsSuccessfulResponse(response))
            {
                await transaction.CommitAsync(CancellationToken.None);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            return response;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

internal static class CommandTransactionRuntime
{
    private static readonly ConcurrentDictionary<Type, bool> CommandRequestCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, bool>> SuccessReaderCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, Error>> ErrorReaderCache = new();

    public static bool IsCommandRequestType(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return CommandRequestCache.GetOrAdd(
            requestType,
            static type =>
                typeof(ICommand).IsAssignableFrom(type) ||
                type.GetInterfaces().Any(static interfaceType =>
                    interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == typeof(ICommand<>)));
    }

    public static CommandTransactionContext CreateContext<TRequest>(TRequest request)
    {
        var moduleKey = request is IModuleScoped moduleScopedRequest
            ? moduleScopedRequest.ModuleKey
            : null;

        return new CommandTransactionContext(typeof(TRequest), moduleKey);
    }

    public static bool IsSuccessfulResponse<TResponse>(TResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return SuccessReaderCache.GetOrAdd(response.GetType(), CreateSuccessReader)(response);
    }

    public static bool TryGetFailure<TResponse>(TResponse response, out Error error)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (IsSuccessfulResponse(response))
        {
            error = Error.None;
            return false;
        }

        error = ErrorReaderCache.GetOrAdd(response.GetType(), CreateErrorReader)(response);
        return error != Error.None;
    }

    private static Func<object, bool> CreateSuccessReader(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return static response => ((Result)response).IsSuccess;
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException(
                $"Command transaction behavior can only evaluate Result or Result<T> responses. Response type {responseType.FullName} is not supported.");
        }

        var isSuccessProperty = responseType.GetProperty(
            nameof(Result.IsSuccess),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Response type {responseType.FullName} does not expose an IsSuccess property.");

        var param = Expression.Parameter(typeof(object), "response");
        var cast = Expression.Convert(param, responseType);
        var prop = Expression.Property(cast, isSuccessProperty);
        return Expression.Lambda<Func<object, bool>>(prop, param).Compile();
    }

    private static Func<object, Error> CreateErrorReader(Type responseType)
    {
        var errorProperty = responseType.GetProperty(
            nameof(Result.Error),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Response type {responseType.FullName} does not expose an Error property.");

        var param = Expression.Parameter(typeof(object), "response");
        var cast = Expression.Convert(param, responseType);
        var prop = Expression.Property(cast, errorProperty);
        var convert = Expression.Convert(prop, typeof(Error));
        return Expression.Lambda<Func<object, Error>>(convert, param).Compile();
    }
}

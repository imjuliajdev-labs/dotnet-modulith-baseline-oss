using System.Collections.Concurrent;
using System.Linq.Expressions;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching;

internal static class DispatcherResponseFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> FailureFactoryCache = new();

    public static TResponse CreateFailure<TResponse>(Error error)
    {
        return (TResponse)CreateFailure(typeof(TResponse), error);
    }

    public static object CreateFailure(Type responseType, Error error)
    {
        if (responseType == typeof(Result))
        {
            return Result.Failure(error);
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException(
                $"Dispatcher failures can only be created for Result or Result<T>. Response type {responseType.FullName} is not supported.");
        }

        var failureFactory = FailureFactoryCache.GetOrAdd(responseType, CreateFailureFactory);
        return failureFactory(error);
    }

    public static Error CombineValidationErrors(IReadOnlyList<Error> errors)
    {
        var concreteErrors = errors
            .Where(static error => error != Error.None)
            .ToArray();

        if (concreteErrors.Length == 0)
        {
            return new Error("validation.failed", "One or more validation failures occurred.", ErrorKind.Validation);
        }

        if (concreteErrors.Length == 1)
        {
            return concreteErrors[0].Kind == ErrorKind.Validation
                ? concreteErrors[0]
                : concreteErrors[0] with { Kind = ErrorKind.Validation };
        }

        var message = string.Join("; ", concreteErrors
            .Select(static error => error.Message)
            .Where(static messagePart => !string.IsNullOrWhiteSpace(messagePart))
            .Distinct(StringComparer.Ordinal));

        return new Error("validation.failed", message, ErrorKind.Validation);
    }

    private static Func<Error, object> CreateFailureFactory(Type responseType)
    {
        var failureMethod = responseType.GetMethod("Failure", [typeof(Error)])
            ?? throw new InvalidOperationException($"Response type {responseType.FullName} does not expose a compatible Failure factory.");

        var errorParameter = Expression.Parameter(typeof(Error), "error");
        var call = Expression.Call(failureMethod, errorParameter);

        return Expression.Lambda<Func<Error, object>>(
            Expression.Convert(call, typeof(object)),
            errorParameter).Compile();
    }
}

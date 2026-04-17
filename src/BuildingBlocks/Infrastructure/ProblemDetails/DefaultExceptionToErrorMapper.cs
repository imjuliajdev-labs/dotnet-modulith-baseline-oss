using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.IntegrationEvents;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

public sealed class DefaultExceptionToErrorMapper : IExceptionToErrorMapper
{
    public Error Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsDbUpdateConcurrencyException(exception))
        {
            return new Error(
                "persistence.concurrency_conflict",
                "The resource was changed by another operation.",
                ErrorKind.Conflict);
        }

        if (TryGetPostgresSqlState(exception, out var sqlState))
        {
            return sqlState switch
            {
                "23505" => new Error(
                    "persistence.unique_constraint",
                    "The operation conflicts with an existing record.",
                    ErrorKind.Conflict),
                "23503" => new Error(
                    "persistence.foreign_key_violation",
                    "A referenced record prevented the operation from completing. Add a request-specific exception handler when this should map to a narrower error.",
                    ErrorKind.Failure),
                _ => new Error(
                    "persistence.postgres_failure",
                    "A database error occurred.",
                    ErrorKind.Failure)
            };
        }

        if (string.Equals(exception.GetType().Name, "AntiforgeryValidationException", StringComparison.Ordinal))
        {
            return new Error(
                "security.antiforgery_invalid",
                "The antiforgery token was missing or invalid.",
                ErrorKind.Validation);
        }

        return exception switch
        {
            UnresolvableIntegrationEventTypeException => new Error(
                "outbox.type_unresolvable",
                "The outbox message references an integration event type that is not loaded in the current composition.",
                ErrorKind.Failure),
            IntegrationEventDeserializationException => new Error(
                "outbox.deserialize_failed",
                "The outbox message payload could not be deserialized to its stored integration event type.",
                ErrorKind.Failure),
            IntegrationEventIdMismatchException => new Error(
                "outbox.event_id_mismatch",
                "The outbox message payload deserialized to an event id that does not match the stored event id.",
                ErrorKind.Failure),
            TimeoutException => new Error(
                "service.timeout",
                "A dependency did not respond before the timeout elapsed.",
                ErrorKind.ServiceUnavailable),
            UnauthorizedAccessException => new Error(
                "auth.forbidden",
                "The current actor is not allowed to perform this action.",
                ErrorKind.Forbidden),
            KeyNotFoundException => new Error(
                "resource.not_found",
                "The requested resource was not found.",
                ErrorKind.NotFound),
            _ => new Error(
                "unexpected.failure",
                "An unexpected error occurred.",
                ErrorKind.Failure)
        };
    }

    private static bool IsDbUpdateConcurrencyException(Exception exception)
    {
        return string.Equals(exception.GetType().Name, "DbUpdateConcurrencyException", StringComparison.Ordinal);
    }

    private static bool TryGetPostgresSqlState(Exception exception, out string? sqlState)
    {
        sqlState = null;

        if (!string.Equals(exception.GetType().Name, "PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        var sqlStateProperty = exception.GetType().GetProperty("SqlState");
        if (sqlStateProperty is null || sqlStateProperty.PropertyType != typeof(string))
        {
            return false;
        }

        sqlState = sqlStateProperty.GetValue(exception) as string;
        return !string.IsNullOrWhiteSpace(sqlState);
    }
}

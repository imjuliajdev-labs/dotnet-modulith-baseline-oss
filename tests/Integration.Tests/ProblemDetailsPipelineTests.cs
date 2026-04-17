using System.Text.Json;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Http;

namespace Integration.Tests;

public sealed class ProblemDetailsPipelineTests
{
    [Xunit.Fact]
    public void DefaultExceptionToErrorMapperMapsCommonExceptionShapes()
    {
        var mapper = new DefaultExceptionToErrorMapper();

        var timeoutError = mapper.Map(new TimeoutException("Timed out."));
        var concurrencyError = mapper.Map(new DbUpdateConcurrencyException());
        var uniqueConstraintError = mapper.Map(new PostgresException("23505"));
        var foreignKeyError = mapper.Map(new PostgresException("23503"));
        var antiforgeryError = mapper.Map(new AntiforgeryValidationException());

        Xunit.Assert.Equal(ErrorKind.ServiceUnavailable, timeoutError.Kind);
        Xunit.Assert.Equal("service.timeout", timeoutError.Code);

        Xunit.Assert.Equal(ErrorKind.Conflict, concurrencyError.Kind);
        Xunit.Assert.Equal("persistence.concurrency_conflict", concurrencyError.Code);

        Xunit.Assert.Equal(ErrorKind.Conflict, uniqueConstraintError.Kind);
        Xunit.Assert.Equal("persistence.unique_constraint", uniqueConstraintError.Code);

        Xunit.Assert.Equal(ErrorKind.Failure, foreignKeyError.Kind);
        Xunit.Assert.Equal("persistence.foreign_key_violation", foreignKeyError.Code);

        Xunit.Assert.Equal(ErrorKind.Validation, antiforgeryError.Kind);
        Xunit.Assert.Equal("security.antiforgery_invalid", antiforgeryError.Code);
    }

    [Xunit.Fact]
    public async Task ProblemDetailsExceptionHandlerWritesTheCanonicalPayload()
    {
        var exceptionHandler = new ProblemDetailsExceptionHandler(
            new DefaultExceptionToErrorMapper(),
            new ProblemDetailsMapper(),
            new ProblemDetailsHttpWriter());

        var httpContext = CreateHttpContext("/throw");
        var handled = await exceptionHandler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("Boom"),
            CancellationToken.None);

        Xunit.Assert.True(handled);
        Xunit.Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);
        Xunit.Assert.Equal("unexpected.failure", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Unexpected failure", json.RootElement.GetProperty("title").GetString());
        Xunit.Assert.Equal("/throw", json.RootElement.GetProperty("instance").GetString());
        Xunit.Assert.Equal(httpContext.TraceIdentifier, json.RootElement.GetProperty("traceId").GetString());
    }

    [Xunit.Fact]
    public async Task ResultHttpMapperLeavesSuccessResultsToTheApiEdgeAndUsesTheSharedWriterForFailures()
    {
        var mapper = new ResultHttpMapper(new ProblemDetailsMapper(), new ProblemDetailsHttpWriter());

        var successContext = CreateHttpContext("/success");
        var expectedSuccessResult = new TestHttpResult(StatusCodes.Status204NoContent, "success");
        var actualSuccessResult = mapper.Match(Result.Success(), successContext, () => expectedSuccessResult);

        Xunit.Assert.Same(expectedSuccessResult, actualSuccessResult);
        await actualSuccessResult.ExecuteAsync(successContext);
        Xunit.Assert.Equal(StatusCodes.Status204NoContent, successContext.Response.StatusCode);

        var failureContext = CreateHttpContext("/conflict");
        var result = Result.Failure(new Error("sample.conflict", "The sample failed.", ErrorKind.Conflict));
        await mapper.Match(result, failureContext, () => expectedSuccessResult).ExecuteAsync(failureContext);

        Xunit.Assert.Equal(StatusCodes.Status409Conflict, failureContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", failureContext.Response.ContentType);

        using var json = await ReadJsonAsync(failureContext);
        Xunit.Assert.Equal("sample.conflict", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Conflict", json.RootElement.GetProperty("title").GetString());
        Xunit.Assert.Equal("/conflict", json.RootElement.GetProperty("instance").GetString());
    }

    private static DefaultHttpContext CreateHttpContext(PathString path)
    {
        var httpContext = new DefaultHttpContext
        {
            Request =
            {
                Path = path
            },
            Response =
            {
                Body = new MemoryStream()
            }
        };

        return httpContext;
    }

    private static async Task<JsonDocument> ReadJsonAsync(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(httpContext.Response.Body);
    }

    private sealed class DbUpdateConcurrencyException : Exception
    {
    }

    private sealed class PostgresException : Exception
    {
        public PostgresException(string sqlState)
        {
            SqlState = sqlState;
        }

        public string SqlState { get; }
    }

    private sealed class AntiforgeryValidationException : Exception
    {
    }

    private sealed class TestHttpResult : IResult
    {
        private readonly string _body;
        private readonly int _statusCode;

        public TestHttpResult(int statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            await httpContext.Response.WriteAsync(_body);
        }
    }
}

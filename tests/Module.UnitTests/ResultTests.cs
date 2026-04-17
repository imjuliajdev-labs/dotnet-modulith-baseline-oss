using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Module.UnitTests;

public sealed class ResultTests
{
    [Fact]
    public void SuccessResultUsesTheEmptyErrorSentinel()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void FailureResultCarriesTheProvidedError()
    {
        var error = new Error("sample.failure", "A sample failure.", ErrorKind.Failure);
        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericResultCarriesItsValueOnSuccess()
    {
        var result = Result<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void SharedDispatcherAndAuditContractsCanBeImplementedByTestDoubles()
    {
        var dispatcher = new StubDispatcher();
        var auditWriter = new StubAuditWriter();
        var moduleInitializer = new StubModuleInitializer();

        Assert.NotNull(dispatcher);
        Assert.NotNull(auditWriter);
        Assert.NotNull(moduleInitializer);
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TResponse>.Failure(new Error("not.implemented", "Not implemented.")));
        }

        public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TResponse>.Failure(new Error("not.implemented", "Not implemented.")));
        }
    }

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubModuleInitializer : IModuleInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

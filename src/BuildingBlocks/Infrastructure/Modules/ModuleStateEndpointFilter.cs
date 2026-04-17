using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Modules;

public sealed class ModuleStateEndpointFilter : IEndpointFilter
{
    private readonly string _moduleKey;

    public ModuleStateEndpointFilter(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        _moduleKey = moduleKey;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var executionGate = httpContext.RequestServices.GetRequiredService<IModuleExecutionGate>();
        await using var execution = await executionGate.TryEnterAsync(_moduleKey, httpContext.RequestAborted);
        if (execution.IsEntered)
        {
            return await next(context);
        }

        var mapper = httpContext.RequestServices.GetRequiredService<ResultHttpMapper>();
        return mapper.Failure(execution.Failure, httpContext);
    }
}

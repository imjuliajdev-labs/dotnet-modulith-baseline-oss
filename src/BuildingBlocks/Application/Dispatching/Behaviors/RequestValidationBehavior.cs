using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class RequestValidationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly IReadOnlyList<IRequestValidator<TRequest>> _validators;

    public RequestValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators?.ToArray() ?? Array.Empty<IRequestValidator<TRequest>>();
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Count == 0)
        {
            return await next(cancellationToken);
        }

        var errors = new List<Error>();
        foreach (var validator in _validators)
        {
            var validationErrors = await validator.ValidateAsync(request, cancellationToken);
            if (validationErrors.Count > 0)
            {
                errors.AddRange(validationErrors);
            }
        }

        if (errors.Count == 0)
        {
            return await next(cancellationToken);
        }

        var combinedError = DispatcherResponseFactory.CombineValidationErrors(errors);
        return DispatcherResponseFactory.CreateFailure<TResponse>(combinedError);
    }
}

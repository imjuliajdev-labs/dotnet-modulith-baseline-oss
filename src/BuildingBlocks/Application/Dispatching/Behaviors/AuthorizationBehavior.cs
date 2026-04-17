using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class AuthorizationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRoleAuthorizer _roleAuthorizer;
    private readonly IRecentAuthenticationAuthorizer _recentAuthenticationAuthorizer;

    public AuthorizationBehavior(
        ICurrentActorAccessor currentActorAccessor,
        IRoleAuthorizer roleAuthorizer,
        IRecentAuthenticationAuthorizer recentAuthenticationAuthorizer)
    {
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _roleAuthorizer = roleAuthorizer ?? throw new ArgumentNullException(nameof(roleAuthorizer));
        _recentAuthenticationAuthorizer = recentAuthenticationAuthorizer ?? throw new ArgumentNullException(nameof(recentAuthenticationAuthorizer));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var authorizeRequest = request as IAuthorizeRequest;
        var recentAuthenticationRequest = request as IRequireRecentAuthentication;

        if (authorizeRequest is null && recentAuthenticationRequest is null)
        {
            return await next(cancellationToken);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (!actor.IsAuthenticated)
        {
            return DispatcherResponseFactory.CreateFailure<TResponse>(AuthorizationErrors.Unauthorized(typeof(TRequest)));
        }

        if (authorizeRequest is not null)
        {
            var requirements = authorizeRequest.AuthorizationRequirements ?? Array.Empty<RoleRequirement>();
            var error = await _roleAuthorizer.GetFailureOrNoneAsync(
                typeof(TRequest),
                actor,
                requirements,
                cancellationToken);

            if (error != Error.None)
            {
                return DispatcherResponseFactory.CreateFailure<TResponse>(error);
            }
        }

        if (recentAuthenticationRequest is not null)
        {
            var error = await _recentAuthenticationAuthorizer.GetFailureOrNoneAsync(
                typeof(TRequest),
                actor,
                recentAuthenticationRequest.RecentAuthenticationWindow,
                cancellationToken);

            if (error != Error.None)
            {
                return DispatcherResponseFactory.CreateFailure<TResponse>(error);
            }
        }

        return await next(cancellationToken);
    }
}

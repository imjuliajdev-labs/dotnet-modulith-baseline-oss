using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Authentication;
using Identity.Application.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Api.Endpoints;

internal static class SignInEndpoints
{
    public static IEndpointRouteBuilder MapSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/session/login",
                static async (
                    PasswordSignInRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new PasswordSignInCommand(request.UserName, request.Password),
                        cancellationToken);
                    var clock = httpContext.RequestServices.GetRequiredService<IClock>();

                    return result.IsFailure
                        ? mapper.Failure(result.Error, httpContext)
                        : new IdentityCookieSignInHttpResult(result.Value
                            ?? throw new InvalidOperationException("Successful sign-in did not return a session payload."),
                            clock);
                })
            .WithName("Identity_PasswordSignIn")
            .WithSummary("Authenticate a browser session with username and password.")
            .Accepts<PasswordSignInRequest>("application/json")
            .Produces<IdentityActorSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(IdentityEndpointPolicies.AuthenticationAttempt);

        endpoints.MapPost(
                "/session/step-up",
                static async (
                    StepUpCurrentActorRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new StepUpCurrentActorCommand(request.Password), cancellationToken);
                    var clock = httpContext.RequestServices.GetRequiredService<IClock>();

                    return result.IsFailure
                        ? mapper.Failure(result.Error, httpContext)
                        : new IdentityCookieSignInHttpResult(result.Value
                            ?? throw new InvalidOperationException("Successful step-up did not return a session payload."),
                            clock);
                })
            .WithName("Identity_StepUpCurrentActor")
            .WithSummary("Refresh the current browser session's recent-auth timestamp using the current actor password.")
            .Accepts<StepUpCurrentActorRequest>("application/json")
            .Produces<IdentityActorSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(IdentityEndpointPolicies.AuthenticationAttempt);

        endpoints.MapPost(
                "/session/password",
                static async (
                    ChangeCurrentActorPasswordRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new ChangeCurrentActorPasswordCommand(request.CurrentPassword, request.NewPassword),
                        cancellationToken);
                    var clock = httpContext.RequestServices.GetRequiredService<IClock>();

                    return result.IsFailure
                        ? mapper.Failure(result.Error, httpContext)
                        : new IdentityCookieSignInHttpResult(result.Value
                            ?? throw new InvalidOperationException("Successful password change did not return a session payload."),
                            clock);
                })
            .WithName("Identity_ChangeCurrentActorPassword")
            .WithSummary("Change the current authenticated actor's password. Requires recent authentication.")
            .Accepts<ChangeCurrentActorPasswordRequest>("application/json")
            .Produces<IdentityActorSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(IdentityEndpointPolicies.PasswordMutation);

        endpoints.MapPost(
                "/session/logout",
                static async (
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new SignOutCommand(), cancellationToken);
                    return result.IsFailure
                        ? mapper.Failure(result.Error, httpContext)
                        : new IdentityCookieSignOutHttpResult();
                })
            .WithName("Identity_SignOut")
            .WithSummary("Terminate the current browser session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}

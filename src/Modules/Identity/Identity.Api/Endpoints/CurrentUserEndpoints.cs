using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Authentication;
using Identity.Application.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class CurrentUserEndpoints
{
    public static IEndpointRouteBuilder MapCurrentUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/me",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetCurrentIdentityActorQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, actor => Results.Ok(IdentityActorSessionResponse.From(actor!)));
                })
            .WithName("Identity_GetCurrentActor")
            .WithSummary("Get the current authenticated browser actor.")
            .Produces<IdentityActorSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet(
                "/me/preferences/time-zone",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetCurrentIdentityActorQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, actor => Results.Ok(PreferredTimeZoneResponse.From(actor!)));
                })
            .WithName("Identity_GetCurrentActorPreferredTimeZone")
            .WithSummary("Get the current authenticated actor's preferred IANA time zone.")
            .Produces<PreferredTimeZoneResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapPut(
                "/me/preferences/time-zone",
                static async (
                    UpdatePreferredTimeZoneRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new SetCurrentActorPreferredTimeZoneCommand(request.PreferredTimeZoneId),
                        cancellationToken);

                    return mapper.Match(result, httpContext, actor => Results.Ok(PreferredTimeZoneResponse.From(actor!)));
                })
            .WithName("Identity_SetCurrentActorPreferredTimeZone")
            .WithSummary("Update the current authenticated actor's preferred IANA time zone.")
            .Accepts<UpdatePreferredTimeZoneRequest>("application/json")
            .Produces<PreferredTimeZoneResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}

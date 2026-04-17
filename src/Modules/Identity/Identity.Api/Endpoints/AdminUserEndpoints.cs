using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Administration;
using Identity.Application.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/users",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListIdentityUsersQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, users => Results.Ok(IdentityUserAccountListResponse.From(users!)));
                })
            .WithName("Identity_ListUsers")
            .WithSummary("List identity users available to the current operator.")
            .Produces<IdentityUserAccountListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Administrator user listing; bounded by the per-request list limit enforced by ListIdentityUsersQueryHandler. Admin-authenticated surface.");

        endpoints.MapPost(
                "/users",
                static async (
                    CreateIdentityUserRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateIdentityUserCommand(
                            request.UserName,
                            request.DisplayName,
                            request.Password,
                            request.Roles,
                            request.PreferredTimeZoneId ?? IdentityUserAdministrationDefaults.DefaultPreferredTimeZoneId,
                            request.Enabled),
                        cancellationToken);

                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_CreateUser")
            .WithSummary("Create a new identity user account.")
            .Accepts<CreateIdentityUserRequest>("application/json")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut(
                "/users/{actorId}/roles",
                static async (
                    string actorId,
                    UpdateIdentityUserRolesRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new SetIdentityUserRolesCommand(actorId, request.Roles), cancellationToken);
                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_SetUserRoles")
            .WithSummary("Replace the role assignments for an identity user account.")
            .Accepts<UpdateIdentityUserRolesRequest>("application/json")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/users/{actorId}/unlock",
                static async (
                    string actorId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new UnlockIdentityUserCommand(actorId), cancellationToken);
                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_UnlockUser")
            .WithSummary("Clear any lockout state on an identity user account.")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut(
                "/users/{actorId}/status",
                static async (
                    string actorId,
                    UpdateIdentityUserStatusRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new SetIdentityUserStatusCommand(actorId, request.Enabled), cancellationToken);
                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_SetUserStatus")
            .WithSummary("Enable or disable an identity user account.")
            .Accepts<UpdateIdentityUserStatusRequest>("application/json")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut(
                "/users/{actorId}/password",
                static async (
                    string actorId,
                    ResetIdentityUserPasswordRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new ResetIdentityUserPasswordCommand(actorId, request.Password), cancellationToken);
                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_ResetUserPassword")
            .WithSummary("Reset the password for an identity user account.")
            .Accepts<ResetIdentityUserPasswordRequest>("application/json")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/users/{actorId}/revoke-sessions",
                static async (
                    string actorId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new RevokeIdentityUserSessionsCommand(actorId), cancellationToken);
                    return mapper.Match(result, httpContext, user => Results.Ok(IdentityUserAccountResponse.From(user!)));
                })
            .WithName("Identity_RevokeUserSessions")
            .WithSummary("Revoke all active browser sessions for an identity user account.")
            .Produces<IdentityUserAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

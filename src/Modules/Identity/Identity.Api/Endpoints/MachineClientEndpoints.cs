using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Administration;
using Identity.Application.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class MachineClientEndpoints
{
    public static IEndpointRouteBuilder MapMachineClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/machine-clients",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListMachineClientsQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, clients => Results.Ok(MachineClientSummaryListResponse.From(clients!)));
                })
            .WithName("Identity_ListMachineClients")
            .WithSummary("List persisted machine clients.")
            .Produces<MachineClientSummaryListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Administrator machine-client registry listing; bounded by the operator-managed machine client cardinality. Admin-authenticated surface.");

        endpoints.MapPost(
                "/machine-clients",
                static async (
                    CreateMachineClientRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateMachineClientCommand(request.ClientName, request.Roles),
                        cancellationToken);

                    return mapper.Match(result, httpContext, created => Results.Ok(MachineClientCreatedResponse.From(created!)));
                })
            .WithName("Identity_CreateMachineClient")
            .WithSummary("Create a new persisted machine client.")
            .Accepts<CreateMachineClientRequest>("application/json")
            .Produces<MachineClientCreatedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet(
                "/machine-clients/{clientId}",
                static async (
                    string clientId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetMachineClientQuery(clientId), cancellationToken);
                    return mapper.Match(result, httpContext, client => Results.Ok(MachineClientSummaryResponse.From(client!)));
                })
            .WithName("Identity_GetMachineClient")
            .WithSummary("Get a persisted machine client by ID.")
            .Produces<MachineClientSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/machine-clients/{clientId}/rotate-secret",
                static async (
                    string clientId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new RotateMachineClientSecretCommand(clientId), cancellationToken);
                    return mapper.Match(result, httpContext, rotated => Results.Ok(MachineClientSecretRotatedResponse.From(rotated!)));
                })
            .WithName("Identity_RotateMachineClientSecret")
            .WithSummary("Rotate the secret for a persisted machine client.")
            .Produces<MachineClientSecretRotatedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/machine-clients/{clientId}/revoke",
                static async (
                    string clientId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new RevokeMachineClientCommand(clientId), cancellationToken);
                    return mapper.Match(result, httpContext, client => Results.Ok(MachineClientSummaryResponse.From(client!)));
                })
            .WithName("Identity_RevokeMachineClient")
            .WithSummary("Revoke a persisted machine client.")
            .Produces<MachineClientSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut(
                "/machine-clients/{clientId}/status",
                static async (
                    string clientId,
                    SetMachineClientStatusRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new SetMachineClientStatusCommand(clientId, request.IsActive), cancellationToken);
                    return mapper.Match(result, httpContext, client => Results.Ok(MachineClientSummaryResponse.From(client!)));
                })
            .WithName("Identity_SetMachineClientStatus")
            .WithSummary("Enable or disable a persisted machine client.")
            .Accepts<SetMachineClientStatusRequest>("application/json")
            .Produces<MachineClientSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

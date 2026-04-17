using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Identity.Application.Authorization;
using NodaTime;

namespace Identity.Application.Administration;

public sealed record MachineClient(
    string ClientId,
    string ClientName,
    string SecretHash,
    bool IsActive,
    bool IsRevoked,
    IReadOnlyCollection<string> Roles,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    Instant? RevokedUtc);

public sealed record MachineClientSummary(
    string ClientId,
    string ClientName,
    bool IsActive,
    bool IsRevoked,
    IReadOnlyCollection<string> Roles,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    Instant? RevokedUtc);

public sealed record MachineClientCreated(
    string ClientId,
    string ClientName,
    string PlaintextSecret,
    IReadOnlyCollection<string> Roles,
    Instant CreatedUtc);

public sealed record MachineClientSecretRotated(
    string ClientId,
    string PlaintextSecret);

public sealed record MachineClientList(IReadOnlyCollection<MachineClientSummary> Clients);

public interface IMachineClientAdministrationService
{
    ValueTask<IReadOnlyCollection<MachineClientSummary>> ListAsync(CancellationToken cancellationToken);

    ValueTask<Result<MachineClientCreated>> CreateAsync(
        string clientName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    ValueTask<Result<MachineClientSecretRotated>> RotateSecretAsync(string clientId, CancellationToken cancellationToken);

    ValueTask<Result<MachineClientSummary>> RevokeAsync(string clientId, CancellationToken cancellationToken);

    ValueTask<Result<MachineClientSummary>> SetActiveAsync(string clientId, bool isActive, CancellationToken cancellationToken);

    ValueTask<Result<MachineClientSummary>> GetAsync(string clientId, CancellationToken cancellationToken);
}

public static class IdentityMachineClientErrors
{
    public static Error ClientNotFound(string clientId)
    {
        return new Error(
            "identity.machine_client_not_found",
            $"Machine client '{clientId}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error ClientAlreadyRevoked(string clientId)
    {
        return new Error(
            "identity.machine_client_already_revoked",
            $"Machine client '{clientId}' has already been revoked.",
            ErrorKind.Validation);
    }

    public static Error ClientNameRequired()
    {
        return new Error(
            "identity.machine_client_name_required",
            "Client name is required.",
            ErrorKind.Validation);
    }

    public static Error ClientNameAlreadyExists(string clientName)
    {
        return new Error(
            "identity.machine_client_name_already_exists",
            $"Machine client '{clientName}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error InvalidRole(string role)
    {
        return new Error(
            "identity.invalid_role",
            $"Role '{role}' is not a recognised identity role. Expected one of Admin, User, Machine.",
            ErrorKind.Validation);
    }
}

public sealed record ListMachineClientsQuery : IQuery<MachineClientList>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListMachineClientsQueryHandler : IQueryHandler<ListMachineClientsQuery, MachineClientList>
{
    private readonly IMachineClientAdministrationService _service;

    public ListMachineClientsQueryHandler(IMachineClientAdministrationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientList>> Handle(ListMachineClientsQuery query, CancellationToken cancellationToken)
    {
        var clients = await _service.ListAsync(cancellationToken);
        return Result<MachineClientList>.Success(new MachineClientList(clients));
    }
}

public sealed record GetMachineClientQuery(string ClientId) : IQuery<MachineClientSummary>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetMachineClientQueryHandler : IQueryHandler<GetMachineClientQuery, MachineClientSummary>
{
    private readonly IMachineClientAdministrationService _service;

    public GetMachineClientQueryHandler(IMachineClientAdministrationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientSummary>> Handle(GetMachineClientQuery query, CancellationToken cancellationToken)
    {
        return await _service.GetAsync(query.ClientId, cancellationToken);
    }
}

public sealed record CreateMachineClientCommand(
    string ClientName,
    IReadOnlyCollection<string> Roles)
    : ICommand<MachineClientCreated>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateMachineClientCommandHandler : ICommandHandler<CreateMachineClientCommand, MachineClientCreated>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IMachineClientAdministrationService _service;

    public CreateMachineClientCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IMachineClientAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientCreated>> Handle(CreateMachineClientCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.CreateAsync(command.ClientName, command.Roles, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.machine_client.create",
                TargetType: "machine_client",
                TargetId: result.Value!.ClientId,
                Outcome: "created",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record RotateMachineClientSecretCommand(string ClientId)
    : ICommand<MachineClientSecretRotated>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class RotateMachineClientSecretCommandHandler : ICommandHandler<RotateMachineClientSecretCommand, MachineClientSecretRotated>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IMachineClientAdministrationService _service;

    public RotateMachineClientSecretCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IMachineClientAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientSecretRotated>> Handle(RotateMachineClientSecretCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.RotateSecretAsync(command.ClientId, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.machine_client.secret.rotate",
                TargetType: "machine_client",
                TargetId: command.ClientId,
                Outcome: "rotated",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record RevokeMachineClientCommand(string ClientId)
    : ICommand<MachineClientSummary>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class RevokeMachineClientCommandHandler : ICommandHandler<RevokeMachineClientCommand, MachineClientSummary>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IMachineClientAdministrationService _service;

    public RevokeMachineClientCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IMachineClientAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientSummary>> Handle(RevokeMachineClientCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.RevokeAsync(command.ClientId, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.machine_client.revoke",
                TargetType: "machine_client",
                TargetId: command.ClientId,
                Outcome: "revoked",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record SetMachineClientStatusCommand(string ClientId, bool IsActive)
    : ICommand<MachineClientSummary>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class SetMachineClientStatusCommandHandler : ICommandHandler<SetMachineClientStatusCommand, MachineClientSummary>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IMachineClientAdministrationService _service;

    public SetMachineClientStatusCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IMachineClientAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<MachineClientSummary>> Handle(SetMachineClientStatusCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.SetActiveAsync(command.ClientId, command.IsActive, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.machine_client.status.update",
                TargetType: "machine_client",
                TargetId: command.ClientId,
                Outcome: command.IsActive ? "activated" : "deactivated",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

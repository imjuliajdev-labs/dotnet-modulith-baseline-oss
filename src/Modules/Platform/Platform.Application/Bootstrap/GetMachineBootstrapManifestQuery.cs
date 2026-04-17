using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Platform.Application.Authorization;

namespace Platform.Application.Bootstrap;

public sealed record GetMachineBootstrapManifestQuery : IQuery<BootstrapManifest>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.AdminOrMachine];
}

internal sealed class GetMachineBootstrapManifestQueryHandler : IQueryHandler<GetMachineBootstrapManifestQuery, BootstrapManifest>
{
    private readonly IEnumerable<IModule> _modules;

    public GetMachineBootstrapManifestQueryHandler(IEnumerable<IModule> modules)
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    }

    public Task<Result<BootstrapManifest>> Handle(GetMachineBootstrapManifestQuery query, CancellationToken cancellationToken)
    {
        var modules = _modules
            .Select(static module => module.Descriptor)
            .OrderBy(static descriptor => descriptor.Key, StringComparer.Ordinal)
            .Select(static descriptor => new BootstrapModuleItem(
                descriptor.Key,
                descriptor.DisplayName,
                descriptor.RoutePrefix,
                descriptor.SchemaName,
                descriptor.ModuleNamespace,
                descriptor.DefaultEnabled,
                descriptor.CanBeDisabled))
            .ToArray();

        return Task.FromResult(Result<BootstrapManifest>.Success(new BootstrapManifest(modules)));
    }
}

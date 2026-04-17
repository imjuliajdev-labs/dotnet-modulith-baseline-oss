using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Platform.Application.Authorization;

namespace Platform.Application.Bootstrap;

public sealed record GetBootstrapManifestQuery : IQuery<BootstrapManifest>, IModuleScoped
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;
}

public sealed record BootstrapManifest(
    IReadOnlyCollection<BootstrapModuleItem> Modules);

public sealed record BootstrapModuleItem(
    string Key,
    string DisplayName,
    string RoutePrefix,
    string SchemaName,
    string ModuleNamespace,
    bool DefaultEnabled,
    bool CanBeDisabled);

internal sealed class GetBootstrapManifestQueryHandler : IQueryHandler<GetBootstrapManifestQuery, BootstrapManifest>
{
    private readonly IEnumerable<IModule> _modules;

    public GetBootstrapManifestQueryHandler(IEnumerable<IModule> modules)
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    }

    public Task<Result<BootstrapManifest>> Handle(GetBootstrapManifestQuery query, CancellationToken cancellationToken)
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

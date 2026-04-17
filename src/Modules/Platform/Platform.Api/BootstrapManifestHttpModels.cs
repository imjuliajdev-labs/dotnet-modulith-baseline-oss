using Platform.Application.Bootstrap;

namespace Platform.Api;

public sealed record BootstrapManifestResponse(
    IReadOnlyCollection<BootstrapManifestModuleResponse> Modules)
{
    public static BootstrapManifestResponse From(BootstrapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new BootstrapManifestResponse(
            manifest.Modules.Select(BootstrapManifestModuleResponse.From).ToArray());
    }
}

public sealed record BootstrapManifestModuleResponse(
    string Key,
    string DisplayName,
    string RoutePrefix,
    string SchemaName,
    string ModuleNamespace,
    bool DefaultEnabled,
    bool CanBeDisabled)
{
    public static BootstrapManifestModuleResponse From(BootstrapModuleItem module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return new BootstrapManifestModuleResponse(
            module.Key,
            module.DisplayName,
            module.RoutePrefix,
            module.SchemaName,
            module.ModuleNamespace,
            module.DefaultEnabled,
            module.CanBeDisabled);
    }
}

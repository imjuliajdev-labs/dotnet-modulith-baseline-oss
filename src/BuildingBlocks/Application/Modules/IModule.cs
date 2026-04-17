namespace BuildingBlocks.Application.Modules;

public interface IModule
{
    ModuleDescriptor Descriptor { get; }

    string Key { get; }
}

public interface IModuleScoped
{
    string ModuleKey { get; }
}

public interface IModuleWorkLeaseManager
{
    ValueTask<IAsyncDisposable> AcquireAsync(string moduleKey, CancellationToken cancellationToken);
}

public interface IModuleInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed record ModuleDescriptor(
    string Key,
    string DisplayName,
    string RoutePrefix,
    string SchemaName,
    string ModuleNamespace,
    bool DefaultEnabled,
    bool CanBeDisabled,
    int CompositionOrder = 100,
    bool HasFrontendSurface = false);

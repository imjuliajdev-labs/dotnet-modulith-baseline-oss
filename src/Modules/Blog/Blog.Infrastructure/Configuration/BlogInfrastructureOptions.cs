namespace Blog.Infrastructure.Configuration;

public sealed class BlogInfrastructureOptions
{
    public const string SectionName = "Modules:Blog";

    public BlogExperienceOptions Experience { get; init; } = new();

    public BlogOperationalOptions Operations { get; init; } = new();
}

public sealed class BlogExperienceOptions
{
    public string Title { get; init; } = "Blog";

    public string Summary { get; init; } = "Editorial publishing surface for the Blog module.";
}

public sealed class BlogOperationalOptions
{
    // Featured posts preview limit for management surfaces.
    public int PreviewLimit { get; init; } = 10;

    public IReadOnlyList<BlogManagedSettingDefinition> OperatorManagedSettings { get; init; } =
    [
        new("OperatorSummary", "Operator summary", "string", "Editorial publishing surface for the Blog module.", true),
        new("PreviewLimit", "Featured posts preview limit", "int", 10, true)
    ];
}

public sealed record BlogManagedSettingDefinition(
    string Name,
    string DisplayName,
    string Type,
    object DefaultValue,
    bool ReloadSafe);

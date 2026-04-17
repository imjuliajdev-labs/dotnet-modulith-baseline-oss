namespace Blog.Api.Configuration;

public sealed class BlogApiOptions
{
    public const string SectionName = "Modules:Blog";

    public BlogApiHttpOptions Http { get; init; } = new();
}

public sealed class BlogApiHttpOptions
{
    public string VersionSetName { get; init; } = "v1";

    public bool EmitProblemDetailsMetadata { get; init; } = true;
}

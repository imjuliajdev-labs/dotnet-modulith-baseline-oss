using Blog.Application.Settings;

namespace Blog.Api;

public sealed record UpdateBlogSettingsRequest(
    int ExpectedVersion,
    string OperatorSummary,
    int PreviewLimit);

public sealed record BlogSettingsResponse(
    string OperatorSummary,
    int PreviewLimit,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId)
{
    public static BlogSettingsResponse From(BlogModuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new BlogSettingsResponse(
            settings.OperatorSummary,
            settings.PreviewLimit,
            settings.Version,
            settings.UpdatedUtc.ToDateTimeOffset(),
            settings.UpdatedByActorId);
    }
}

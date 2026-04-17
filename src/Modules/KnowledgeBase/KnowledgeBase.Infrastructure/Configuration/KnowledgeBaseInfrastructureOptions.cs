namespace KnowledgeBase.Infrastructure.Configuration;

public sealed class KnowledgeBaseInfrastructureOptions
{
    public const string SectionName = "Modules:KnowledgeBase";

    public KnowledgeBaseSettingsOptions Settings { get; init; } = new();
}

public sealed class KnowledgeBaseSettingsOptions
{
    public string PublicExperienceTitle { get; init; } = KnowledgeBase.Application.Settings.KnowledgeBaseModuleSettingsDefaults.DefaultPublicExperienceTitle;

    public string PublicExperienceBlurb { get; init; } = KnowledgeBase.Application.Settings.KnowledgeBaseModuleSettingsDefaults.DefaultPublicExperienceBlurb;

    public string SearchPlaceholder { get; init; } = KnowledgeBase.Application.Settings.KnowledgeBaseModuleSettingsDefaults.DefaultSearchPlaceholder;

    public bool SearchEnabled { get; init; } = true;

    public int ManagementPreviewLimit { get; init; } = KnowledgeBase.Application.Settings.KnowledgeBaseModuleSettingsDefaults.DefaultManagementPreviewLimit;
}

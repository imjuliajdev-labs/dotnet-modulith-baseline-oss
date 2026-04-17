namespace SampleFeature.Domain.Announcements;

public enum SampleAnnouncementContentValidationError
{
    None = 0,
    TitleRequired = 1,
    BodyRequired = 2
}

public sealed record SampleAnnouncementContent(string Title, string Body)
{
    public static bool TryCreate(
        string? title,
        string? body,
        out SampleAnnouncementContent? content,
        out SampleAnnouncementContentValidationError error)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            content = null;
            error = SampleAnnouncementContentValidationError.TitleRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            content = null;
            error = SampleAnnouncementContentValidationError.BodyRequired;
            return false;
        }

        content = new SampleAnnouncementContent(title.Trim(), body.Trim());
        error = SampleAnnouncementContentValidationError.None;
        return true;
    }
}

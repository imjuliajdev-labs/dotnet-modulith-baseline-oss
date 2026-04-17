using SampleFeature.Domain.Announcements;

namespace Module.UnitTests.ModuleCoverage.SampleFeature;

public sealed class SampleAnnouncementContentTests
{
    [Xunit.Fact]
    public void TryCreate_TrimsValues_AndReturnsNormalizedContent()
    {
        var created = SampleAnnouncementContent.TryCreate(
            "  Release Window  ",
            "  Service maintenance tonight.  ",
            out var content,
            out var error);

        Xunit.Assert.True(created);
        Xunit.Assert.Equal(SampleAnnouncementContentValidationError.None, error);
        Xunit.Assert.NotNull(content);
        Xunit.Assert.Equal("Release Window", content!.Title);
        Xunit.Assert.Equal("Service maintenance tonight.", content.Body);
    }

    [Xunit.Fact]
    public void TryCreate_RejectsWhitespaceTitleBeforeBody()
    {
        var created = SampleAnnouncementContent.TryCreate(
            "   ",
            "Body",
            out var content,
            out var error);

        Xunit.Assert.False(created);
        Xunit.Assert.Null(content);
        Xunit.Assert.Equal(SampleAnnouncementContentValidationError.TitleRequired, error);
    }

    [Xunit.Fact]
    public void TryCreate_RejectsWhitespaceBody()
    {
        var created = SampleAnnouncementContent.TryCreate(
            "Title",
            "   ",
            out var content,
            out var error);

        Xunit.Assert.False(created);
        Xunit.Assert.Null(content);
        Xunit.Assert.Equal(SampleAnnouncementContentValidationError.BodyRequired, error);
    }
}

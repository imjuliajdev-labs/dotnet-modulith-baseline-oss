using Blog.Application.Posts;
using Blog.Domain.Posts;

namespace Module.UnitTests.ModuleCoverage.Blog;

public sealed class BlogPostNormalizationTests
{
    [Xunit.Fact]
    public void DraftFactory_UsesTitleToCreateSlugAndNormalizesEditorialValues()
    {
        var created = BlogPostDraft.TryCreate(
            slug: null,
            title: "  Shipping the governed blog  ",
            summary: "  First real production slice.  ",
            body: "  Rich content now lives with the post.  ",
            categorySlug: " Platform Strategy ",
            tagNames: ["Architecture", " publishing ", "architecture"],
            seoMetadata: new BlogSeoMetadata("  SEO title  ", "  SEO description  ", "  contracts, blog  "),
            shareTargets: ["LinkedIn", " email newsletter "],
            draft: out var draft,
            error: out var error);

        Xunit.Assert.True(created);
        Xunit.Assert.Equal(BlogPostDraftValidationError.None, error);
        Xunit.Assert.NotNull(draft);
        Xunit.Assert.Equal("shipping-the-governed-blog", draft!.Slug);
        Xunit.Assert.Equal("Shipping the governed blog", draft.Title);
        Xunit.Assert.Equal("First real production slice.", draft.Summary);
        Xunit.Assert.Equal("Rich content now lives with the post.", draft.Body);
        Xunit.Assert.Equal("platform-strategy", draft.CategorySlug);
        Xunit.Assert.Equal(["architecture", "publishing"], draft.TagNames);
        Xunit.Assert.Equal(["email-newsletter", "linkedin"], draft.ShareTargets);
        Xunit.Assert.Equal("SEO title", draft.SeoMetadata.Title);
        Xunit.Assert.Equal("SEO description", draft.SeoMetadata.Description);
        Xunit.Assert.Equal("contracts, blog", draft.SeoMetadata.Keywords);
    }

    [Xunit.Fact]
    public void DraftFactory_RejectsWhitespaceTitleBeforeSummaryAndBody()
    {
        var created = BlogPostDraft.TryCreate(
            slug: "ignored",
            title: "   ",
            summary: "Has a summary",
            body: "Has a body",
            categorySlug: null,
            tagNames: null,
            seoMetadata: new BlogSeoMetadata(null, null, null),
            shareTargets: null,
            draft: out _,
            error: out var error);

        Xunit.Assert.False(created);
        Xunit.Assert.Equal(BlogPostDraftValidationError.TitleRequired, error);
    }

    [Xunit.Fact]
    public void StatusNames_MapPublishedStatusToThePublicContractValue()
    {
        Xunit.Assert.Equal("published", BlogPostStatusNames.From(BlogPostStatus.Published));
    }
}

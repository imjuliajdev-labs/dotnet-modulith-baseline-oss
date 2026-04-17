using KnowledgeBase.Application.Entries;
using KnowledgeBase.Domain.Entries;
using NodaTime;

namespace KnowledgeBase.Infrastructure.Persistence;

internal static class KnowledgeBaseSeedData
{
    public static IReadOnlyCollection<KnowledgeEntry> Create(Instant now)
    {
        return
        [
            CreateEntry(
                Guid.Parse("0c5ca0d0-3c21-4f38-b4bc-2d73b73636f3"),
                "what-is-this-baseline",
                "What is this baseline?",
                "This baseline is a governed modular monolith reference that demonstrates module boundaries, generated contracts, runtime module controls, and a real browser shell.",
                "Getting started",
                featured: true,
                sortOrder: 10,
                now),
            CreateEntry(
                Guid.Parse("41b933c7-55c6-49e1-8a2a-3f204068f18b"),
                "how-do-i-add-a-module",
                "How do I add a module?",
                "Use the governed module scaffold in scripts/New-Module.ps1 with an explicit module spec so structure and tests land in the supported shape.",
                "Getting started",
                featured: false,
                sortOrder: 20,
                now)
        ];
    }

    private static KnowledgeEntry CreateEntry(
        Guid entryId,
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        Instant now)
    {
        return new KnowledgeEntry(
            entryId,
            slug,
            title,
            body,
            category,
            featured,
            sortOrder,
            KnowledgeEntryStatus.Published,
            Version: 1,
            CreatedUtc: now,
            UpdatedUtc: now,
            UpdatedByActorId: "system:knowledge-base-seed",
            PublishedUtc: now,
            PublishedByActorId: "system:knowledge-base-seed");
    }
}

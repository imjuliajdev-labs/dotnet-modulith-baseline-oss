using System.Text.Json;
using NodaTime;

namespace Architecture.Tests;

public sealed class WaiverValidationTests
{
    [Fact]
    public void ActiveWaiversReferenceValidRuleIds()
    {
        var waivers = ReadActiveWaivers();
        if (waivers.Count == 0)
        {
            return;
        }

        var catalog = RepositoryFiles.ReadRuleCatalog();
        var validRuleIds = new HashSet<string>(
            catalog.Select(static entry => entry.RuleId),
            StringComparer.Ordinal);

        var violations = waivers
            .Where(waiver => !validRuleIds.Contains(waiver.RuleId))
            .Select(waiver => $"Waiver '{waiver.WaiverId}' references unknown rule '{waiver.RuleId}'.")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ActiveWaiversAreNotExpired()
    {
        var waivers = ReadActiveWaivers();
        if (waivers.Count == 0)
        {
            return;
        }

        var now = SystemClock.Instance.GetCurrentInstant().ToDateTimeOffset();

        var violations = waivers
            .Where(waiver => waiver.ExpiresUtc <= now)
            .Select(waiver => $"Waiver '{waiver.WaiverId}' for rule '{waiver.RuleId}' expired on {waiver.ExpiresUtc:O}.")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ActiveWaiversHaveRequiredFields()
    {
        var waivers = ReadActiveWaivers();
        if (waivers.Count == 0)
        {
            return;
        }

        var violations = new List<string>();

        foreach (var waiver in waivers)
        {
            if (string.IsNullOrWhiteSpace(waiver.WaiverId))
            {
                violations.Add($"A waiver entry is missing 'waiver_id'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.RuleId))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'rule_id'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.Scope))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'scope'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.Owner))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'owner'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.Reason))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'reason'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.LinkedFollowUp))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'linked_follow_up'.");
            }

            if (string.IsNullOrWhiteSpace(waiver.RemovalCondition))
            {
                violations.Add($"Waiver '{waiver.WaiverId}' is missing 'removal_condition'.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ActiveWaiverIdsAreUnique()
    {
        var waivers = ReadActiveWaivers();
        if (waivers.Count == 0)
        {
            return;
        }

        var duplicates = waivers
            .GroupBy(static waiver => waiver.WaiverId, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"Duplicate waiver id '{group.Key}' appears {group.Count()} times.")
            .ToArray();

        Assert.Empty(duplicates);
    }

    private static IReadOnlyList<WaiverEntry> ReadActiveWaivers()
    {
        var path = RepositoryFiles.PathFromRoot("waivers", "active.waivers.json");
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<WaiverFile>(json, JsonOptions());
        return file?.Waivers ?? [];
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };
    }

    private sealed class WaiverFile
    {
        public List<WaiverEntry> Waivers { get; set; } = [];
    }

    private sealed class WaiverEntry
    {
        public string WaiverId { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset ExpiresUtc { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string LinkedFollowUp { get; set; } = string.Empty;
        public string RemovalCondition { get; set; } = string.Empty;
    }
}

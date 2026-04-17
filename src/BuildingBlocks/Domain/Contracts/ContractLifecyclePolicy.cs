using System.Globalization;
using System.Text.RegularExpressions;

namespace BuildingBlocks.Domain.Contracts;

public static class ContractLifecyclePolicy
{
    public const int MaxCompatibilityWindowDays = 365;

    private const string IsoDateFormat = "yyyy-MM-dd";

    private static readonly Regex VersionedNamePattern = new(
        @"^(?<base>.+?)V(?<version>\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsPastRetirementDeadline(ContractLifecycleAttribute lifecycle, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (lifecycle.Status != ContractLifecycleStatus.Superseded)
        {
            return false;
        }

        if (!TryParseIsoDate(lifecycle.RetireOn, out var retireOn))
        {
            return false;
        }

        return retireOn < today;
    }

    public static bool TryParseIsoDate(string? value, out DateOnly result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return DateOnly.TryParseExact(value, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    public static string ExtractBaseVersionName(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var match = VersionedNamePattern.Match(typeName);
        return match.Success ? match.Groups["base"].Value : typeName;
    }
}

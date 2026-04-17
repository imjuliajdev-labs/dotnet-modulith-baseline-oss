using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authentication;

public sealed class SeededMachineCredentialAuthenticator
{
    private readonly MachineAuthenticationAuditWriter _auditWriter;
    private readonly SeededMachineOptions _options;

    public SeededMachineCredentialAuthenticator(
        MachineAuthenticationAuditWriter auditWriter,
        IOptions<SeededMachineOptions> seededMachineOptions)
    {
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        ArgumentNullException.ThrowIfNull(seededMachineOptions);

        _options = seededMachineOptions.Value ?? new SeededMachineOptions();
    }

    internal async ValueTask<MachineCredentialAuthenticationAttempt> TryAuthenticateAsync(string providedApiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providedApiKey);

        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || !string.Equals(providedApiKey, _options.ApiKey, StringComparison.Ordinal))
        {
            return MachineCredentialAuthenticationAttempt.NotHandled;
        }

        await _auditWriter.WriteAsync(_options.ClientId, "succeeded_seeded", _options.ActorId, cancellationToken);
        return MachineCredentialAuthenticationAttempt.Succeeded(
            _options.ActorId,
            _options.ClientId,
            ResolveSeededRoles(_options.Roles));
    }

    private static IReadOnlyCollection<string> ResolveSeededRoles(string[]? configured)
    {
        if (configured is null || configured.Length == 0)
        {
            return new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine };
        }

        var normalized = configured
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Where(IdentityAccountSupport.IsKnownRole)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0
            ? new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine }
            : normalized;
    }
}

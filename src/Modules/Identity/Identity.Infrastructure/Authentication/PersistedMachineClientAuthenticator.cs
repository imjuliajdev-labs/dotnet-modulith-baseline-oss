using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Authentication;

public sealed class PersistedMachineClientAuthenticator
{
    private const string ClientIdPrefix = "machine:client:";

    private readonly MachineAuthenticationAuditWriter _auditWriter;
    private readonly IdentityPersistenceDbContext _dbContext;
    private readonly PasswordHasher<IdentityMachineClientRecord> _passwordHasher = new();

    public PersistedMachineClientAuthenticator(
        MachineAuthenticationAuditWriter auditWriter,
        IdentityPersistenceDbContext dbContext)
    {
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    internal async ValueTask<MachineCredentialAuthenticationAttempt> TryAuthenticateAsync(string providedApiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providedApiKey);

        if (!TryParsePersistedCredential(providedApiKey, out var clientId, out var secret))
        {
            return MachineCredentialAuthenticationAttempt.NotHandled;
        }

        var client = await _dbContext.MachineClients
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.ClientId == clientId, cancellationToken);

        if (client is null)
        {
            await _auditWriter.WriteAsync(clientId, "failed_invalid_credentials", actorId: null, cancellationToken);
            return MachineCredentialAuthenticationAttempt.Failed("Invalid machine credentials.");
        }

        if (client.IsRevoked)
        {
            await _auditWriter.WriteAsync(clientId, "failed_revoked", actorId: null, cancellationToken);
            return MachineCredentialAuthenticationAttempt.Failed("Machine client is revoked.");
        }

        if (!client.IsActive)
        {
            await _auditWriter.WriteAsync(clientId, "failed_inactive", actorId: null, cancellationToken);
            return MachineCredentialAuthenticationAttempt.Failed("Machine client is inactive.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(client, client.SecretHash, secret);
        if (verification == PasswordVerificationResult.Failed)
        {
            await _auditWriter.WriteAsync(clientId, "failed_invalid_credentials", actorId: null, cancellationToken);
            return MachineCredentialAuthenticationAttempt.Failed("Invalid machine credentials.");
        }

        var roles = client.Roles.Length > 0
            ? client.Roles
            : new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine };

        await _auditWriter.WriteAsync(client.ClientId, "succeeded_persisted", client.ClientId, cancellationToken);
        return MachineCredentialAuthenticationAttempt.Succeeded(client.ClientId, client.ClientName, roles);
    }

    private static bool TryParsePersistedCredential(string providedApiKey, out string clientId, out string secret)
    {
        clientId = string.Empty;
        secret = string.Empty;

        if (!providedApiKey.StartsWith(ClientIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var secretSeparatorIndex = providedApiKey.IndexOf(':', ClientIdPrefix.Length);
        if (secretSeparatorIndex <= 0 || secretSeparatorIndex >= providedApiKey.Length - 1)
        {
            return false;
        }

        clientId = providedApiKey[..secretSeparatorIndex];
        secret = providedApiKey[(secretSeparatorIndex + 1)..];
        return true;
    }
}

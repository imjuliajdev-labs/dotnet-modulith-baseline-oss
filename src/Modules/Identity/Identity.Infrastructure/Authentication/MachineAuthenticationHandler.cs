using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authentication;

public static class IdentityMachineAuthenticationDefaults
{
    public const string SchemeName = "Machine";
    public const string ApiKeyHeaderName = "X-Machine-Key";
}

public sealed class SeededMachineOptions
{
    public string ActorId { get; set; } = "machine:seeded-client";

    public string ClientId { get; set; } = "baseline-machine-client";

    public string? ApiKey { get; set; }

    public string[]? Roles { get; set; }
}

public sealed class MachineAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly MachineAuthenticationAuditWriter _auditWriter;
    private readonly PersistedMachineClientAuthenticator _persistedMachineClientAuthenticator;
    private readonly SeededMachineCredentialAuthenticator _seededMachineCredentialAuthenticator;

    public MachineAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        MachineAuthenticationAuditWriter auditWriter,
        SeededMachineCredentialAuthenticator seededMachineCredentialAuthenticator,
        PersistedMachineClientAuthenticator persistedMachineClientAuthenticator)
        : base(schemeOptions, logger, encoder)
    {
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _seededMachineCredentialAuthenticator = seededMachineCredentialAuthenticator ?? throw new ArgumentNullException(nameof(seededMachineCredentialAuthenticator));
        _persistedMachineClientAuthenticator = persistedMachineClientAuthenticator ?? throw new ArgumentNullException(nameof(persistedMachineClientAuthenticator));
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(IdentityMachineAuthenticationDefaults.ApiKeyHeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var providedApiKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.NoResult();
        }

        var seededAttempt = await _seededMachineCredentialAuthenticator.TryAuthenticateAsync(providedApiKey, Context.RequestAborted);
        if (seededAttempt.Kind != MachineCredentialAuthenticationAttemptKind.NotHandled)
        {
            return ToAuthenticateResult(seededAttempt);
        }

        var persistedAttempt = await _persistedMachineClientAuthenticator.TryAuthenticateAsync(providedApiKey, Context.RequestAborted);
        if (persistedAttempt.Kind != MachineCredentialAuthenticationAttemptKind.NotHandled)
        {
            return ToAuthenticateResult(persistedAttempt);
        }

        await _auditWriter.WriteAsync(targetId: "unknown", outcome: "failed_invalid_credentials", actorId: null, Context.RequestAborted);
        return AuthenticateResult.Fail("Invalid machine credentials.");
    }

    private AuthenticateResult ToAuthenticateResult(MachineCredentialAuthenticationAttempt attempt)
    {
        return attempt.Kind switch
        {
            MachineCredentialAuthenticationAttemptKind.Succeeded => AuthenticateResult.Success(CreateTicket(
                attempt.ActorId ?? throw new InvalidOperationException("Successful machine authentication is missing an actor id."),
                attempt.PrincipalName ?? throw new InvalidOperationException("Successful machine authentication is missing a principal name."),
                attempt.Roles)),
            MachineCredentialAuthenticationAttemptKind.Failed => AuthenticateResult.Fail(attempt.FailureMessage ?? "Invalid machine credentials."),
            _ => AuthenticateResult.NoResult()
        };
    }

    private static AuthenticationTicket CreateTicket(string actorId, string principalName, IReadOnlyCollection<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId),
            new(ClaimTypes.Name, principalName)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, IdentityMachineAuthenticationDefaults.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, IdentityMachineAuthenticationDefaults.SchemeName);
    }
}

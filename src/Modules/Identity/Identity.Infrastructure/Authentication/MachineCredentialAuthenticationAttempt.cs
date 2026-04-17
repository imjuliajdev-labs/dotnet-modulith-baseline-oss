namespace Identity.Infrastructure.Authentication;

internal enum MachineCredentialAuthenticationAttemptKind
{
    NotHandled,
    Succeeded,
    Failed
}

internal sealed record MachineCredentialAuthenticationAttempt(
    MachineCredentialAuthenticationAttemptKind Kind,
    string? ActorId,
    string? PrincipalName,
    IReadOnlyCollection<string> Roles,
    string? FailureMessage)
{
    public static MachineCredentialAuthenticationAttempt NotHandled { get; } =
        new(MachineCredentialAuthenticationAttemptKind.NotHandled, null, null, Array.Empty<string>(), null);

    public static MachineCredentialAuthenticationAttempt Succeeded(
        string actorId,
        string principalName,
        IReadOnlyCollection<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalName);
        ArgumentNullException.ThrowIfNull(roles);

        return new MachineCredentialAuthenticationAttempt(
            MachineCredentialAuthenticationAttemptKind.Succeeded,
            actorId,
            principalName,
            roles,
            null);
    }

    public static MachineCredentialAuthenticationAttempt Failed(string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new MachineCredentialAuthenticationAttempt(
            MachineCredentialAuthenticationAttemptKind.Failed,
            null,
            null,
            Array.Empty<string>(),
            failureMessage);
    }
}

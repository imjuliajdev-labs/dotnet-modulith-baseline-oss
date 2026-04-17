namespace Identity.PublicContracts.Queries;

public interface IIdentityTimeZonePreferenceQueryService
{
    ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken);
}

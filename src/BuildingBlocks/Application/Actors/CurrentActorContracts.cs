using NodaTime;

namespace BuildingBlocks.Application.Actors;

public sealed class CurrentActor
{
    private readonly HashSet<string> _roles;

    public CurrentActor(string? actorId, bool isAuthenticated, IEnumerable<string>? roles = null, Instant? authenticatedAt = null)
    {
        ActorId = string.IsNullOrWhiteSpace(actorId)
            ? null
            : actorId;
        IsAuthenticated = isAuthenticated;
        AuthenticatedAt = authenticatedAt;
        _roles = roles is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                roles.Where(static role => !string.IsNullOrWhiteSpace(role)),
                StringComparer.OrdinalIgnoreCase);
    }

    public static CurrentActor Anonymous { get; } = new(actorId: null, isAuthenticated: false);

    public string? ActorId { get; }

    public bool IsAuthenticated { get; }

    public Instant? AuthenticatedAt { get; }

    public IReadOnlyCollection<string> Roles => _roles;

    public bool IsInRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return _roles.Contains(role);
    }
}

public interface ICurrentActorAccessor
{
    ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken);
}

internal sealed class AnonymousCurrentActorAccessor : ICurrentActorAccessor
{
    public ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CurrentActor.Anonymous);
    }
}

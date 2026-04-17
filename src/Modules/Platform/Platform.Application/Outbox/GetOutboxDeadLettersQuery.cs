using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Platform.Application.Authorization;

namespace Platform.Application.Outbox;

public sealed record OutboxDeadLetterSummary(IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry> Entries);

public sealed record GetOutboxDeadLettersQuery(int Limit = 50) : IQuery<OutboxDeadLetterSummary>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetOutboxDeadLettersQueryHandler : IQueryHandler<GetOutboxDeadLettersQuery, OutboxDeadLetterSummary>
{
    private readonly IEnumerable<IIntegrationEventOutboxStore> _outboxStores;

    public GetOutboxDeadLettersQueryHandler(IEnumerable<IIntegrationEventOutboxStore> outboxStores)
    {
        _outboxStores = outboxStores ?? throw new ArgumentNullException(nameof(outboxStores));
    }

    public async Task<Result<OutboxDeadLetterSummary>> Handle(GetOutboxDeadLettersQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit <= 0
            ? 50
            : Math.Min(query.Limit, 200);

        var allEntries = new List<IntegrationEventOutboxDeadLetterEntry>();

        foreach (var store in _outboxStores)
        {
            var entries = await store.GetDeadLetteredAsync(limit, cancellationToken);
            allEntries.AddRange(entries);
        }

        var result = allEntries
            .OrderByDescending(static entry => entry.DeadLetteredUtc)
            .Take(limit)
            .ToArray();

        return Result<OutboxDeadLetterSummary>.Success(new OutboxDeadLetterSummary(result));
    }
}

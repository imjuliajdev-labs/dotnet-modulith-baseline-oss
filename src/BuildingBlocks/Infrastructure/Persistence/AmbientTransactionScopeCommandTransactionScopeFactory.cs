using System.Transactions;
using BuildingBlocks.Application.Dispatching;

namespace BuildingBlocks.Infrastructure.Persistence;

internal sealed class AmbientTransactionScopeCommandTransactionScopeFactory : ICommandTransactionScopeFactory
{
    private readonly HashSet<string> _transactionalModules;

    public AmbientTransactionScopeCommandTransactionScopeFactory(IEnumerable<ICommandTransactionParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);

        _transactionalModules = participants
            .Select(static participant => participant.ModuleKey?.Trim())
            .Where(static moduleKey => !string.IsNullOrWhiteSpace(moduleKey))
            .Select(static moduleKey => moduleKey!)
            .ToHashSet(StringComparer.Ordinal);
    }

    public ValueTask<ICommandTransactionScope> BeginAsync(CommandTransactionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ModuleKey) || !_transactionalModules.Contains(context.ModuleKey.Trim()))
        {
            return ValueTask.FromResult<ICommandTransactionScope>(NoOpScope.Instance);
        }

        var transactionScope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = IsolationLevel.ReadCommitted,
                Timeout = TransactionManager.DefaultTimeout
            },
            TransactionScopeAsyncFlowOption.Enabled);

        return ValueTask.FromResult<ICommandTransactionScope>(new TransactionScopeAdapter(transactionScope));
    }

    private sealed class TransactionScopeAdapter : ICommandTransactionScope
    {
        private readonly TransactionScope _transactionScope;
        private bool _completed;

        public TransactionScopeAdapter(TransactionScope transactionScope)
        {
            _transactionScope = transactionScope;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            if (!_completed)
            {
                _transactionScope.Complete();
                _completed = true;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _transactionScope.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpScope : ICommandTransactionScope
    {
        public static NoOpScope Instance { get; } = new();

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

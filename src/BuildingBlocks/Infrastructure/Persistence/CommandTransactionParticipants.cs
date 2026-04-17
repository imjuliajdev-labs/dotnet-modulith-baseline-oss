namespace BuildingBlocks.Infrastructure.Persistence;

public interface ICommandTransactionParticipant
{
    string ModuleKey { get; }
}

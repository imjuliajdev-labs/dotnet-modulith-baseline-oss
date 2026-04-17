namespace BuildingBlocks.Infrastructure.Persistence;

public interface IDbContextRollbackRegistration
{
    string ModuleKey { get; }
    Type DbContextType { get; }
}

public sealed class DbContextRollbackRegistration<TDbContext> : IDbContextRollbackRegistration
{
    public DbContextRollbackRegistration(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ModuleKey = moduleKey;
    }

    public string ModuleKey { get; }
    public Type DbContextType => typeof(TDbContext);
}

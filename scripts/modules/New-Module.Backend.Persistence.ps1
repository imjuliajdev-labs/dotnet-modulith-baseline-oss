function New-PersistenceDefaultsContent {
@"
namespace ${moduleName}.Infrastructure.Persistence;

public static class ${modulePersistenceDefaultsTypeName}
{
    public const string ConnectionStringName = "BaselineDatabase";

    public const string SchemaName = "$([string]$spec['schemaName'])";

    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";
}
"@
}

function New-PersistenceDbContextContent {
@"
using Microsoft.EntityFrameworkCore;

namespace ${moduleName}.Infrastructure.Persistence;

public sealed class ${modulePersistenceDbContextTypeName} : DbContext
{
    public ${modulePersistenceDbContextTypeName}(DbContextOptions<${modulePersistenceDbContextTypeName}> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(${modulePersistenceDefaultsTypeName}.SchemaName);

        // Keep configuration application explicit so the module's first real table arrives intentionally.
        // ${moduleEntityTypeConfigurationExampleTypeName} shows the baseline pattern and a persistence-only record type but is not applied by default.
        // When the first real mapping is ready, replace this comment with a call such as:
        // modelBuilder.ApplyConfiguration(new SomeAggregatePersistenceConfiguration());
    }
}
"@
}

function New-EntityTypeConfigurationExampleContent {
@"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ${moduleName}.Infrastructure.Persistence;

// Replace this scaffold example with the module's first real persistence mapping before creating migrations.
internal sealed class ${moduleEntityTypeConfigurationExampleTypeName} : IEntityTypeConfiguration<${moduleScaffoldedPersistenceRecordTypeName}>
{
    public void Configure(EntityTypeBuilder<${moduleScaffoldedPersistenceRecordTypeName}> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("replace_before_first_migration");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Version).IsConcurrencyToken();
    }
}

internal sealed class ${moduleScaffoldedPersistenceRecordTypeName}
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int Version { get; set; }
}
"@
}

function New-PersistenceOptionsExtensionsContent {
@"
using Microsoft.EntityFrameworkCore;

namespace ${moduleName}.Infrastructure.Persistence;

internal static class ${moduleName}PersistenceOptionsExtensions
{
    public static DbContextOptionsBuilder Use${moduleName}Persistence(
        this DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable(
                    ${modulePersistenceDefaultsTypeName}.MigrationsHistoryTableName,
                    ${modulePersistenceDefaultsTypeName}.SchemaName);
            });
    }
}
"@
}

function New-PersistenceDbContextFactoryContent {
@"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ${moduleName}.Infrastructure.Persistence;

public sealed class ${modulePersistenceDbContextFactoryTypeName} : IDesignTimeDbContextFactory<${modulePersistenceDbContextTypeName}>
{
    public ${modulePersistenceDbContextTypeName} CreateDbContext(string[] args)
    {
        var connectionString = TryGetConnectionStringFromArguments(args)
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__" + ${modulePersistenceDefaultsTypeName}.ConnectionStringName)
            ?? "Host=localhost;Database=baseline;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<${modulePersistenceDbContextTypeName}>();
        optionsBuilder.Use${moduleName}Persistence(connectionString);

        return new ${modulePersistenceDbContextTypeName}(optionsBuilder.Options);
    }

    private static string? TryGetConnectionStringFromArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--connection-string", StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length
                    ? args[index + 1]
                    : null;
            }

            const string commandLinePrefix = "--connection-string=";
            if (args[index].StartsWith(commandLinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[index][commandLinePrefix.Length..];
            }
        }

        return null;
    }
}
"@
}

function New-DatabaseMigrationContent {
@"
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ${moduleName}.Infrastructure.Persistence;

internal sealed class ${moduleDatabaseMigrationTypeName} : IDatabaseMigration
{
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<${modulePersistenceDbContextTypeName}> _dbContextFactory;

    public ${moduleDatabaseMigrationTypeName}(
        IConfiguration configuration,
        IDbContextFactory<${modulePersistenceDbContextTypeName}> dbContextFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public string Name => ${modulePersistenceDefaultsTypeName}.SchemaName;

    public string? ConnectionString => _configuration.GetConnectionString(${modulePersistenceDefaultsTypeName}.ConnectionStringName);

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return Array.Empty<string>();
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
"@
}

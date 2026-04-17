using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Blog.Infrastructure.Persistence;

public sealed class BlogPersistenceDbContextFactory : IDesignTimeDbContextFactory<BlogPersistenceDbContext>
{
    public BlogPersistenceDbContext CreateDbContext(string[] args)
    {
        var connectionString = TryGetConnectionStringFromArguments(args)
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__" + BlogPersistenceDefaults.ConnectionStringName)
            ?? "Host=localhost;Database=baseline;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<BlogPersistenceDbContext>();
        optionsBuilder.UseBlogPersistence(connectionString);

        return new BlogPersistenceDbContext(optionsBuilder.Options);
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

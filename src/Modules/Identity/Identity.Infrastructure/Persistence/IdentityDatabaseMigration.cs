using BuildingBlocks.Infrastructure.Persistence;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Persistence;

internal sealed class IdentityDatabaseMigration : IDatabaseMigration
{
    private readonly IConfiguration _configuration;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly IDbContextFactory<IdentityPersistenceDbContext> _dbContextFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILookupNormalizer _lookupNormalizer = new UpperInvariantLookupNormalizer();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SeededAdminOptions _seededAdminOptions;
    private readonly SeededMachineOptions _seededMachineOptions;

    public IdentityDatabaseMigration(
        IConfiguration configuration,
        BuildingBlocks.Domain.Time.IClock clock,
        IDbContextFactory<IdentityPersistenceDbContext> dbContextFactory,
        IHostEnvironment hostEnvironment,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<SeededAdminOptions> seededAdminOptions,
        IOptions<SeededMachineOptions> seededMachineOptions)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        ArgumentNullException.ThrowIfNull(seededAdminOptions);
        ArgumentNullException.ThrowIfNull(seededMachineOptions);
        _seededAdminOptions = seededAdminOptions.Value ?? new SeededAdminOptions();
        _seededMachineOptions = seededMachineOptions.Value ?? new SeededMachineOptions();
    }

    public string Name => IdentityPersistenceDefaults.SchemaName;

    public string? ConnectionString => _configuration.GetConnectionString(IdentityPersistenceDefaults.ConnectionStringName);

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

        IdentityBootstrapCredentialPolicy.EnsureConfiguredSeededCredentialsAllowed(
            _hostEnvironment,
            _seededAdminOptions,
            _seededMachineOptions);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);

        await using (var scope = _serviceScopeFactory.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await EnsureRolesAsync(roleManager);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityAccount>>();
            await EnsureSeededAdminAsync(userManager, cancellationToken);
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles =
        [
            BuildingBlocks.Application.Authorization.IdentityRoles.Admin,
            BuildingBlocks.Application.Authorization.IdentityRoles.User,
            BuildingBlocks.Application.Authorization.IdentityRoles.Machine
        ];

        foreach (var role in roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var createResult = await roleManager.CreateAsync(new IdentityRole(role));
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed identity role '{role}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            }
        }
    }

    private async Task EnsureSeededAdminAsync(
        UserManager<IdentityAccount> userManager,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var normalizedUserName = IdentityAccountSupport.NormalizeUserName(_seededAdminOptions.UserName) ?? "admin";
        var normalizedDisplayName = IdentityAccountSupport.NormalizeDisplayName(_seededAdminOptions.DisplayName) ?? "Baseline Admin";
        var actorId = string.IsNullOrWhiteSpace(_seededAdminOptions.ActorId)
            ? "identity:seeded-admin"
            : _seededAdminOptions.ActorId.Trim();
        var now = _clock.GetCurrentInstant();

        var user = await userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            user = new IdentityAccount
            {
                Id = actorId,
                UserName = normalizedUserName,
                NormalizedUserName = _lookupNormalizer.NormalizeName(normalizedUserName),
                DisplayName = normalizedDisplayName,
                Enabled = true,
                LockoutEnabled = true,
                PreferredTimeZoneId = Identity.Application.Administration.IdentityUserAdministrationDefaults.DefaultPreferredTimeZoneId,
                CreatedUtc = now,
                UpdatedUtc = now,
                SecurityStamp = Guid.NewGuid().ToString("n"),
                ConcurrencyStamp = Guid.NewGuid().ToString("n")
            };

            var seededPassword = _seededAdminOptions.Password;
            if (!string.IsNullOrWhiteSpace(seededPassword))
            {
                var createResult = await userManager.CreateAsync(user, seededPassword);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed identity admin: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
                }
            }
            else
            {
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed identity admin: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
                }
            }
        }
        else
        {
            user.UserName = normalizedUserName;
            user.NormalizedUserName = _lookupNormalizer.NormalizeName(normalizedUserName);
            user.DisplayName = normalizedDisplayName;
            user.Enabled = true;
            user.LockoutEnabled = true;
            user.PreferredTimeZoneId = string.IsNullOrWhiteSpace(user.PreferredTimeZoneId)
                ? Identity.Application.Administration.IdentityUserAdministrationDefaults.DefaultPreferredTimeZoneId
                : user.PreferredTimeZoneId;
            user.UpdatedUtc = now;
            await userManager.UpdateAsync(user);

            var seededPassword = _seededAdminOptions.Password;
            if (!string.IsNullOrWhiteSpace(seededPassword))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await userManager.ResetPasswordAsync(user, token, seededPassword);
                if (!resetResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to refresh seeded admin password: {string.Join("; ", resetResult.Errors.Select(e => e.Description))}");
                }
            }
        }

        if (!await userManager.IsInRoleAsync(user, BuildingBlocks.Application.Authorization.IdentityRoles.Admin))
        {
            var addResult = await userManager.AddToRoleAsync(user, BuildingBlocks.Application.Authorization.IdentityRoles.Admin);
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to grant Admin role to seeded admin: {string.Join("; ", addResult.Errors.Select(e => e.Description))}");
            }
        }
    }
}

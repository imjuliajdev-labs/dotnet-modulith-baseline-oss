using BuildingBlocks.Infrastructure.Persistence;
using Identity.Application.Administration;
using Identity.Application.Authentication;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Identity.PublicContracts.Queries;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Identity.Infrastructure;

public static class IdentityInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();

        services.AddOptions<SeededAdminOptions>()
            .BindConfiguration("Modules:Identity:SeededAdmin");
        services.AddOptions<SeededMachineOptions>()
            .BindConfiguration("Modules:Identity:SeededMachine");

        services.AddDbContextFactory<IdentityPersistenceDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var connectionString = configuration.GetConnectionString(IdentityPersistenceDefaults.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseIdentityPersistence(connectionString);
            }
        });

        services.TryAddScoped(static provider =>
            provider.GetRequiredService<IDbContextFactory<IdentityPersistenceDbContext>>().CreateDbContext());

        services.AddIdentityCore<IdentityAccount>(options =>
            {
                options.Password.RequiredLength = IdentityAccountSupport.MinimumPasswordLength;
                options.Password.RequiredUniqueChars = IdentityAccountSupport.RequiredUniqueCharacterCount;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = IdentityAccountSupport.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = IdentityAccountSupport.DefaultLockoutTimeSpan;
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = false;
            })
            .AddClaimsPrincipalFactory<IdentityAccountClaimsPrincipalFactory>()
            .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole>()
            .AddEntityFrameworkStores<IdentityPersistenceDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.Name = "__Host-dotnet-modulith-baseline";
                options.Cookie.Path = "/";
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                options.EventsType = typeof(IdentityCookieAuthenticationEvents);
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

        services.TryAddScoped<IdentityCookieAuthenticationEvents>();
        services.TryAddScoped<IIdentityCookieAuthenticationService, IdentityCookieAuthenticationService>();

        services.TryAddScoped<IIdentityAccountStore, EfIdentityAccountStore>();
        services.TryAddScoped<IIdentityPasswordSignInService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityCurrentActorReader>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityCurrentActorPreferenceService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityCurrentActorStepUpService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityCurrentActorPasswordService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityUserAdministrationService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());
        services.TryAddScoped<IIdentityTimeZonePreferenceQueryService>(static provider => provider.GetRequiredService<IIdentityAccountStore>());

        services.TryAddScoped<EfMachineClientStore>();
        services.TryAddScoped<IMachineClientAdministrationService>(static provider => provider.GetRequiredService<EfMachineClientStore>());
        services.TryAddScoped<MachineAuthenticationAuditWriter>();
        services.TryAddScoped<SeededMachineCredentialAuthenticator>();
        services.TryAddScoped<PersistedMachineClientAuthenticator>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IdentityBootstrapCredentialValidationHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, IdentityDatabaseMigration>());

        return services;
    }
}

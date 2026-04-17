using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace Identity.Infrastructure.Persistence;

internal static class IdentityPersistenceDefaults
{
    public const string ConnectionStringName = "BaselineDatabase";
    public const string SchemaName = "identity";
    public const string AccountsTableName = "accounts";
    public const string AccountClaimsTableName = "account_claims";
    public const string AccountLoginsTableName = "account_logins";
    public const string AccountTokensTableName = "account_tokens";
    public const string MachineClientsTableName = "machine_clients";
    public const string RolesTableName = "roles";
    public const string AccountRolesTableName = "account_roles";
    public const string RoleClaimsTableName = "role_claims";
}

public sealed class IdentityPersistenceDbContext
    : IdentityDbContext<IdentityAccount, IdentityRole, string>
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    private static readonly ValueConverter<Instant?, DateTimeOffset?> NullableInstantValueConverter = new(
        static instant => instant.HasValue ? instant.Value.ToDateTimeOffset() : null,
        static dateTimeOffset => dateTimeOffset.HasValue ? Instant.FromDateTimeOffset(dateTimeOffset.Value) : null);

    public IdentityPersistenceDbContext(DbContextOptions<IdentityPersistenceDbContext> options)
        : base(options)
    {
    }

    internal DbSet<IdentityMachineClientRecord> MachineClients => Set<IdentityMachineClientRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(IdentityPersistenceDefaults.SchemaName);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityAccount>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.AccountsTableName);
            builder.Property(record => record.Id).HasColumnName("actor_id");
            builder.Property(record => record.UserName).HasColumnName("user_name").HasMaxLength(256);
            builder.Property(record => record.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
            builder.Property(record => record.Email).HasColumnName("email").HasMaxLength(256);
            builder.Property(record => record.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            builder.Property(record => record.EmailConfirmed).HasColumnName("email_confirmed");
            builder.Property(record => record.PasswordHash).HasColumnName("password_hash").HasColumnType("text");
            builder.Property(record => record.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(256);
            builder.Property(record => record.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(256).IsConcurrencyToken();
            builder.Property(record => record.PhoneNumber).HasColumnName("phone_number").HasMaxLength(64);
            builder.Property(record => record.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            builder.Property(record => record.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            builder.Property(record => record.LockoutEnd).HasColumnName("lockout_end");
            builder.Property(record => record.LockoutEnabled).HasColumnName("lockout_enabled");
            builder.Property(record => record.AccessFailedCount).HasColumnName("access_failed_count");
            builder.Property(record => record.DisplayName).HasColumnName("display_name").HasMaxLength(256);
            builder.Property(record => record.Enabled).HasColumnName("enabled");
            builder.Property(record => record.PreferredTimeZoneId).HasColumnName("preferred_time_zone_id").HasMaxLength(128);
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.HasIndex(record => record.NormalizedEmail).HasDatabaseName("ix_identity_accounts_normalized_email");
            builder.HasIndex(record => record.NormalizedUserName).IsUnique().HasDatabaseName("ux_identity_accounts_normalized_user_name");
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.AccountClaimsTableName);
            builder.Property(record => record.Id).HasColumnName("id");
            builder.Property(record => record.UserId).HasColumnName("actor_id");
            builder.Property(record => record.ClaimType).HasColumnName("claim_type").HasMaxLength(256);
            builder.Property(record => record.ClaimValue).HasColumnName("claim_value").HasColumnType("text");
            builder.HasIndex(record => new { record.UserId, record.ClaimType }).HasDatabaseName("ix_identity_account_claims_actor_id_claim_type");
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.AccountLoginsTableName);
            builder.Property(record => record.UserId).HasColumnName("actor_id");
            builder.Property(record => record.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
            builder.Property(record => record.ProviderKey).HasColumnName("provider_key").HasMaxLength(256);
            builder.Property(record => record.ProviderDisplayName).HasColumnName("provider_display_name").HasMaxLength(256);
        });

        modelBuilder.Entity<IdentityUserToken<string>>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.AccountTokensTableName);
            builder.Property(record => record.UserId).HasColumnName("actor_id");
            builder.Property(record => record.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
            builder.Property(record => record.Name).HasColumnName("name").HasMaxLength(128);
            builder.Property(record => record.Value).HasColumnName("value").HasColumnType("text");
        });

        modelBuilder.Entity<IdentityRole>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.RolesTableName);
            builder.Property(record => record.Id).HasColumnName("id");
            builder.Property(record => record.Name).HasColumnName("name").HasMaxLength(256);
            builder.Property(record => record.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
            builder.Property(record => record.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(256).IsConcurrencyToken();
        });

        modelBuilder.Entity<IdentityUserRole<string>>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.AccountRolesTableName);
            builder.Property(record => record.UserId).HasColumnName("actor_id");
            builder.Property(record => record.RoleId).HasColumnName("role_id");
        });

        modelBuilder.Entity<IdentityRoleClaim<string>>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.RoleClaimsTableName);
            builder.Property(record => record.Id).HasColumnName("id");
            builder.Property(record => record.RoleId).HasColumnName("role_id");
            builder.Property(record => record.ClaimType).HasColumnName("claim_type").HasMaxLength(256);
            builder.Property(record => record.ClaimValue).HasColumnName("claim_value").HasColumnType("text");
        });

        modelBuilder.Entity<IdentityMachineClientRecord>(builder =>
        {
            builder.ToTable(IdentityPersistenceDefaults.MachineClientsTableName);
            builder.HasKey(record => record.ClientId);
            builder.Property(record => record.ClientId).HasColumnName("client_id");
            builder.Property(record => record.ClientName).HasColumnName("client_name").HasMaxLength(256);
            builder.Property(record => record.NormalizedClientName).HasColumnName("normalized_client_name").HasMaxLength(256);
            builder.Property(record => record.SecretHash).HasColumnName("secret_hash").HasColumnType("text");
            builder.Property(record => record.IsActive).HasColumnName("is_active");
            builder.Property(record => record.IsRevoked).HasColumnName("is_revoked");
            builder.Property(record => record.Roles).HasColumnName("roles").HasColumnType("text[]");
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.RevokedUtc).HasColumnName("revoked_utc").HasConversion(NullableInstantValueConverter);
            builder.HasIndex(record => record.NormalizedClientName).IsUnique().HasDatabaseName("ux_identity_machine_clients_normalized_client_name");
        });
    }
}

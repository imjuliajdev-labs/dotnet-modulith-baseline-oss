using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformPersistenceDbContext))]
[Migration("20260403120000_InitialPlatformOperationalStore")]
public sealed class InitialPlatformOperationalStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(
            name: PlatformPersistenceDefaults.SchemaName);

        migrationBuilder.CreateTable(
            name: PlatformPersistenceDefaults.AuditEventsTableName,
            schema: PlatformPersistenceDefaults.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ModuleKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Outcome = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", static table => table.Id);
            });

        migrationBuilder.CreateTable(
            name: PlatformPersistenceDefaults.ModuleStateChangesTableName,
            schema: PlatformPersistenceDefaults.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ModuleKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                BeforeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AfterState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ChangedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_module_state_changes", static table => table.Id);
            });

        migrationBuilder.CreateTable(
            name: PlatformPersistenceDefaults.ModuleStatesTableName,
            schema: PlatformPersistenceDefaults.SchemaName,
            columns: table => new
            {
                ModuleKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RuntimeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_module_states", static table => table.ModuleKey);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_OccurredUtc",
            schema: PlatformPersistenceDefaults.SchemaName,
            table: PlatformPersistenceDefaults.AuditEventsTableName,
            column: "OccurredUtc");

        migrationBuilder.CreateIndex(
            name: "IX_module_state_changes_ModuleKey_ChangedUtc",
            schema: PlatformPersistenceDefaults.SchemaName,
            table: PlatformPersistenceDefaults.ModuleStateChangesTableName,
            columns: new[] { "ModuleKey", "ChangedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: PlatformPersistenceDefaults.AuditEventsTableName,
            schema: PlatformPersistenceDefaults.SchemaName);

        migrationBuilder.DropTable(
            name: PlatformPersistenceDefaults.ModuleStateChangesTableName,
            schema: PlatformPersistenceDefaults.SchemaName);

        migrationBuilder.DropTable(
            name: PlatformPersistenceDefaults.ModuleStatesTableName,
            schema: PlatformPersistenceDefaults.SchemaName);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasDefaultSchema(PlatformPersistenceDefaults.SchemaName)
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Platform.Infrastructure.Persistence.PlatformAuditEventRecord", builder =>
        {
            builder.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint");

            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(builder.Property<long>("Id"));

            builder.Property<string>("Action")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.Property<string>("ActorId")
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            builder.Property<string>("CorrelationId")
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            builder.Property<string>("ModuleKey")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.Property<DateTimeOffset>("OccurredUtc")
                .HasColumnType("timestamp with time zone");

            builder.Property<string>("Outcome")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.Property<string>("TargetId")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            builder.Property<string>("TargetType")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.HasKey("Id");

            builder.HasIndex("OccurredUtc");

            builder.ToTable(PlatformPersistenceDefaults.AuditEventsTableName, PlatformPersistenceDefaults.SchemaName);
        });

        modelBuilder.Entity("Platform.Infrastructure.Persistence.PlatformModuleStateChangeRecord", builder =>
        {
            builder.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint");

            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(builder.Property<long>("Id"));

            builder.Property<ModuleRuntimeState>("AfterState")
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)");

            builder.Property<string>("ActorId")
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            builder.Property<ModuleRuntimeState>("BeforeState")
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)");

            builder.Property<DateTimeOffset>("ChangedUtc")
                .HasColumnType("timestamp with time zone");

            builder.Property<string>("ModuleKey")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.HasKey("Id");

            builder.HasIndex("ModuleKey", "ChangedUtc");

            builder.ToTable(PlatformPersistenceDefaults.ModuleStateChangesTableName, PlatformPersistenceDefaults.SchemaName);
        });

        modelBuilder.Entity("Platform.Infrastructure.Persistence.PlatformModuleStateRecord", builder =>
        {
            builder.Property<string>("ModuleKey")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.Property<ModuleRuntimeState>("RuntimeState")
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)");

            builder.HasKey("ModuleKey");

            builder.ToTable(PlatformPersistenceDefaults.ModuleStatesTableName, PlatformPersistenceDefaults.SchemaName);
        });
#pragma warning restore 612, 618
    }
}

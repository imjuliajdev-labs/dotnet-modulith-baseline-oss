using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdoptAspNetCoreIdentityEfCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS identity;

                CREATE TABLE IF NOT EXISTS identity.accounts
                (
                    actor_id TEXT PRIMARY KEY,
                    user_name CHARACTER VARYING(256) NULL,
                    display_name CHARACTER VARYING(256) NOT NULL,
                    password_hash TEXT NULL,
                    enabled BOOLEAN NOT NULL,
                    permission_snapshot_version CHARACTER VARYING(64) NOT NULL,
                    preferred_time_zone_id CHARACTER VARYING(128) NOT NULL DEFAULT 'Etc/UTC',
                    created_utc TIMESTAMPTZ NOT NULL,
                    updated_utc TIMESTAMPTZ NOT NULL
                );

                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS normalized_user_name CHARACTER VARYING(256) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS email CHARACTER VARYING(256) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS normalized_email CHARACTER VARYING(256) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS email_confirmed BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS security_stamp CHARACTER VARYING(256) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS concurrency_stamp CHARACTER VARYING(256) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS permissions TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[];
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS phone_number CHARACTER VARYING(64) NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS phone_number_confirmed BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS two_factor_enabled BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS lockout_end TIMESTAMPTZ NULL;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS lockout_enabled BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE identity.accounts ADD COLUMN IF NOT EXISTS access_failed_count INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE identity.accounts ALTER COLUMN preferred_time_zone_id SET DEFAULT 'Etc/UTC';
                ALTER TABLE identity.accounts ALTER COLUMN permissions SET DEFAULT ARRAY[]::TEXT[];

                UPDATE identity.accounts
                SET preferred_time_zone_id = 'Etc/UTC'
                WHERE preferred_time_zone_id IS NULL OR btrim(preferred_time_zone_id) = '';

                UPDATE identity.accounts
                SET permissions = ARRAY[]::TEXT[]
                WHERE permissions IS NULL;

                UPDATE identity.accounts
                SET normalized_user_name = UPPER(user_name)
                WHERE (normalized_user_name IS NULL OR btrim(normalized_user_name) = '')
                    AND user_name IS NOT NULL
                    AND btrim(user_name) <> '';

                UPDATE identity.accounts
                SET security_stamp = permission_snapshot_version
                WHERE (security_stamp IS NULL OR btrim(security_stamp) = '')
                    AND permission_snapshot_version IS NOT NULL
                    AND btrim(permission_snapshot_version) <> '';

                UPDATE identity.accounts
                SET security_stamp = actor_id
                WHERE security_stamp IS NULL OR btrim(security_stamp) = '';

                UPDATE identity.accounts
                SET concurrency_stamp = actor_id
                WHERE concurrency_stamp IS NULL OR btrim(concurrency_stamp) = '';

                CREATE INDEX IF NOT EXISTS ix_identity_accounts_normalized_email
                    ON identity.accounts (normalized_email);

                CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_accounts_normalized_user_name
                    ON identity.accounts (normalized_user_name);

                CREATE TABLE IF NOT EXISTS identity.machine_clients
                (
                    client_id TEXT PRIMARY KEY,
                    client_name CHARACTER VARYING(256) NOT NULL,
                    secret_hash TEXT NOT NULL,
                    is_active BOOLEAN NOT NULL,
                    is_revoked BOOLEAN NOT NULL,
                    permissions TEXT[] NOT NULL,
                    permission_snapshot_version CHARACTER VARYING(64) NOT NULL,
                    created_utc TIMESTAMPTZ NOT NULL,
                    updated_utc TIMESTAMPTZ NOT NULL,
                    revoked_utc TIMESTAMPTZ NULL
                );

                ALTER TABLE identity.machine_clients ADD COLUMN IF NOT EXISTS normalized_client_name CHARACTER VARYING(256) NULL;

                UPDATE identity.machine_clients
                SET normalized_client_name = UPPER(client_name)
                WHERE (normalized_client_name IS NULL OR btrim(normalized_client_name) = '')
                    AND client_name IS NOT NULL
                    AND btrim(client_name) <> '';

                CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_machine_clients_normalized_client_name
                    ON identity.machine_clients (normalized_client_name);

                CREATE TABLE IF NOT EXISTS identity.account_claims
                (
                    id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    actor_id TEXT NOT NULL REFERENCES identity.accounts(actor_id) ON DELETE CASCADE,
                    claim_type CHARACTER VARYING(256) NULL,
                    claim_value TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_identity_account_claims_actor_id_claim_type
                    ON identity.account_claims (actor_id, claim_type);

                CREATE OR REPLACE FUNCTION identity.sync_permission_claims_to_legacy_permissions(target_actor_id TEXT)
                RETURNS void
                LANGUAGE sql
                AS $$
                    UPDATE identity.accounts AS account
                    SET permissions = COALESCE(
                        (
                            SELECT ARRAY_AGG(permission_value ORDER BY permission_value)
                            FROM
                            (
                                SELECT DISTINCT claim.claim_value AS permission_value
                                FROM identity.account_claims AS claim
                                WHERE claim.actor_id = target_actor_id
                                    AND claim.claim_type = 'permission'
                                    AND claim.claim_value IS NOT NULL
                                    AND btrim(claim.claim_value) <> ''
                            ) AS permission_values
                        ),
                        ARRAY[]::TEXT[])
                    WHERE account.actor_id = target_actor_id;
                $$;

                CREATE OR REPLACE FUNCTION identity.sync_legacy_permissions_to_permission_claims(target_actor_id TEXT)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM identity.account_claims
                    WHERE actor_id = target_actor_id
                        AND claim_type = 'permission';

                    INSERT INTO identity.account_claims (actor_id, claim_type, claim_value)
                    SELECT target_actor_id, 'permission', permission_value
                    FROM
                    (
                        SELECT DISTINCT unnest(account.permissions) AS permission_value
                        FROM identity.accounts AS account
                        WHERE account.actor_id = target_actor_id
                    ) AS permission_values
                    WHERE permission_value IS NOT NULL
                        AND btrim(permission_value) <> '';
                END $$;

                CREATE OR REPLACE FUNCTION identity.trg_sync_permission_claims_to_legacy_permissions()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF TG_OP <> 'INSERT' AND OLD.claim_type = 'permission' THEN
                        PERFORM identity.sync_permission_claims_to_legacy_permissions(OLD.actor_id);
                    END IF;

                    IF TG_OP <> 'DELETE'
                        AND NEW.claim_type = 'permission'
                        AND (TG_OP = 'INSERT'
                            OR NEW.actor_id IS DISTINCT FROM OLD.actor_id
                            OR NEW.claim_type IS DISTINCT FROM OLD.claim_type
                            OR NEW.claim_value IS DISTINCT FROM OLD.claim_value) THEN
                        PERFORM identity.sync_permission_claims_to_legacy_permissions(NEW.actor_id);
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END $$;

                CREATE OR REPLACE FUNCTION identity.trg_sync_legacy_permissions_to_permission_claims()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        RETURN NEW;
                    END IF;

                    PERFORM identity.sync_legacy_permissions_to_permission_claims(NEW.actor_id);
                    RETURN NEW;
                END $$;

                DROP TRIGGER IF EXISTS trg_sync_permission_claims_to_legacy_permissions ON identity.account_claims;

                CREATE TRIGGER trg_sync_permission_claims_to_legacy_permissions
                AFTER INSERT OR UPDATE OR DELETE ON identity.account_claims
                FOR EACH ROW
                EXECUTE FUNCTION identity.trg_sync_permission_claims_to_legacy_permissions();

                DROP TRIGGER IF EXISTS trg_sync_legacy_permissions_to_permission_claims ON identity.accounts;

                CREATE TRIGGER trg_sync_legacy_permissions_to_permission_claims
                AFTER INSERT OR UPDATE OF permissions ON identity.accounts
                FOR EACH ROW
                EXECUTE FUNCTION identity.trg_sync_legacy_permissions_to_permission_claims();

                CREATE TABLE IF NOT EXISTS identity.account_logins
                (
                    login_provider CHARACTER VARYING(128) NOT NULL,
                    provider_key CHARACTER VARYING(256) NOT NULL,
                    provider_display_name CHARACTER VARYING(256) NULL,
                    actor_id TEXT NOT NULL REFERENCES identity.accounts(actor_id) ON DELETE CASCADE,
                    CONSTRAINT pk_account_logins PRIMARY KEY (login_provider, provider_key)
                );

                CREATE INDEX IF NOT EXISTS ix_account_logins_actor_id
                    ON identity.account_logins (actor_id);

                CREATE TABLE IF NOT EXISTS identity.account_tokens
                (
                    actor_id TEXT NOT NULL REFERENCES identity.accounts(actor_id) ON DELETE CASCADE,
                    login_provider CHARACTER VARYING(128) NOT NULL,
                    name CHARACTER VARYING(128) NOT NULL,
                    value TEXT NULL,
                    CONSTRAINT pk_account_tokens PRIMARY KEY (actor_id, login_provider, name)
                );

                INSERT INTO identity.account_claims (actor_id, claim_type, claim_value)
                SELECT account.actor_id, 'permission', permission_value
                FROM identity.accounts AS account
                CROSS JOIN LATERAL unnest(account.permissions) AS permission_value
                WHERE permission_value IS NOT NULL
                    AND btrim(permission_value) <> ''
                    AND NOT EXISTS (
                        SELECT 1
                        FROM identity.account_claims AS claim
                        WHERE claim.actor_id = account.actor_id
                            AND claim.claim_type = 'permission'
                            AND claim.claim_value = permission_value);

                SELECT identity.sync_permission_claims_to_legacy_permissions(account.actor_id)
                FROM identity.accounts AS account;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_permission_claims_to_legacy_permissions ON identity.account_claims;
                DROP TRIGGER IF EXISTS trg_sync_legacy_permissions_to_permission_claims ON identity.accounts;
                DROP FUNCTION IF EXISTS identity.trg_sync_permission_claims_to_legacy_permissions();
                DROP FUNCTION IF EXISTS identity.trg_sync_legacy_permissions_to_permission_claims();
                DROP FUNCTION IF EXISTS identity.sync_permission_claims_to_legacy_permissions(TEXT);
                DROP FUNCTION IF EXISTS identity.sync_legacy_permissions_to_permission_claims(TEXT);
                """);

            migrationBuilder.DropTable(
                name: "account_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "account_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "account_tokens",
                schema: "identity");

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS identity.ix_identity_accounts_normalized_email;
                DROP INDEX IF EXISTS identity.ux_identity_accounts_normalized_user_name;
                DROP INDEX IF EXISTS identity.ux_identity_machine_clients_normalized_client_name;

                ALTER TABLE IF EXISTS identity.machine_clients
                    DROP COLUMN IF EXISTS normalized_client_name;

                ALTER TABLE IF EXISTS identity.accounts
                    DROP COLUMN IF EXISTS normalized_user_name,
                    DROP COLUMN IF EXISTS email,
                    DROP COLUMN IF EXISTS normalized_email,
                    DROP COLUMN IF EXISTS email_confirmed,
                    DROP COLUMN IF EXISTS security_stamp,
                    DROP COLUMN IF EXISTS concurrency_stamp,
                    DROP COLUMN IF EXISTS phone_number,
                    DROP COLUMN IF EXISTS phone_number_confirmed,
                    DROP COLUMN IF EXISTS two_factor_enabled,
                    DROP COLUMN IF EXISTS lockout_end,
                    DROP COLUMN IF EXISTS lockout_enabled,
                    DROP COLUMN IF EXISTS access_failed_count;
                """);
        }
    }
}

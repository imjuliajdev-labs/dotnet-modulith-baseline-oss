# Secret Management Convention

This document defines the production secret-management pattern for module authors in this modulith baseline.

## Principles

1. **Secrets never live in source control.** No `appsettings.json`, `.env` file, or checked-in configuration should contain production secrets.
2. **Configuration is the single entry point.** All secrets are consumed through `IConfiguration`, regardless of their source.
3. **Module authors use connection string and option naming conventions.** This makes it possible to swap secret sources without changing module code.

## Naming Convention

| Secret Type | Configuration Key Pattern | Example |
|------------|---------------------------|---------|
| Database connection string | `ConnectionStrings:{Name}` | `ConnectionStrings:BaselineDatabase` |
| Module-specific secret | `Modules:{ModuleName}:{SecretName}` | `Modules:Identity:SeededAdmin:Password` |
| External service credential | `ExternalServices:{ServiceName}:{CredentialName}` | `ExternalServices:Stripe:ApiKey` |
| Machine-to-machine auth | `Modules:{ModuleName}:{ClientType}:ApiKey` | `Modules:Identity:SeededMachine:ApiKey` |

## Environment-Specific Sources

### Local Development

Use `appsettings.Development.json` or environment variables for local-only credentials. The seeded admin password and machine API key in this baseline are Development/Testing-only values.

On the default composed runtime, seeded identity bootstrap credentials are rejected outside `Development` and `Testing`. There is no override flag; any non-Development/non-Testing environment configured with seeded credentials will fail fast at startup. The seeded machine bootstrap credential carries role assignments drawn from the `IdentityRole` pool (typically `Machine`, optionally `Admin`) via `Modules:Identity:SeededMachine:Roles`; broader automation should use persisted machine clients created through the machine-client administration endpoints instead.

For the local backend path in [`../README.md`](../README.md), the minimum useful setup is usually:

- `ConnectionStrings__BaselineDatabase`
- `Modules__Identity__SeededAdmin__Password`
- optionally `Modules__Identity__SeededMachine__ApiKey` plus `Modules__Identity__SeededMachine__Roles__0=Machine` when you want to exercise the default machine-auth smoke

Example `appsettings.Development.json` shape:

```json
{
  "Modules": {
    "Identity": {
      "SeededAdmin": {
        "Password": "LocalOnly!123"
      }
    }
  }
}
```

### Docker Compose Development

Docker Compose uses a `.env` file at the repository root for credential values:

1. Copy the template: `cp .env.example .env`
2. Edit `.env` with your local development credentials if you do not want the checked-in Development defaults
3. `.env` is gitignored and must not be committed

For first-time OSS evaluation, the checked-in `.env.example` already contains usable Development defaults such as `SEEDED_ADMIN_PASSWORD=LocalOnly!123` and `SEEDED_MACHINE_API_KEY=MachineOnly!123`. The `docker-compose.yml` file references these variables via `${VARIABLE_NAME}` syntax; the committed compose file still avoids hardcoded credentials while giving adopters a working local bootstrap path as soon as they copy `.env.example` to `.env`.

### CI / Test

Use in-memory configuration in test harnesses (`PostgresBackedApiApplication.CreateConfiguration`) with test-only values. Never reuse production secrets in CI. The integration harness runs under the `Testing` environment so seeded test credentials stay inside the governed dev/test posture.

### Staging / Production

Use one of these secret providers, injected into `IConfiguration` at startup:

| Provider | Integration |
|----------|------------|
| **Azure Key Vault** | `builder.Configuration.AddAzureKeyVault(...)` |
| **AWS Secrets Manager** | `builder.Configuration.AddSecretsManager(...)` |
| **HashiCorp Vault** | Custom `IConfigurationProvider` or sidecar agent |
| **Kubernetes Secrets** | Mounted as environment variables or files |
| **Docker Secrets** | Mounted at `/run/secrets/` and read via file-based provider |

### Example: Azure Key Vault Integration

```csharp
// In Program.cs or a composition root extension
if (!builder.Environment.IsDevelopment())
{
    var vaultUri = builder.Configuration["KeyVault:Uri"];
    if (!string.IsNullOrWhiteSpace(vaultUri))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(vaultUri),
            new DefaultAzureCredential());
    }
}
```

Key Vault secret names map to configuration keys with `--` replacing `:`:

| Key Vault Secret Name | Configuration Key |
|----------------------|-------------------|
| `ConnectionStrings--BaselineDatabase` | `ConnectionStrings:BaselineDatabase` |
| `Modules--Identity--SeededMachine--ApiKey` | `Modules:Identity:SeededMachine:ApiKey` |

## Module Author Checklist

When adding a new secret to a module:

1. Consume it through `IConfiguration` using the naming convention above.
2. Fail fast at startup if a required secret is missing (throw from the service constructor or a hosted service).
3. Document the required configuration key in your module's `README` or inline with the option class.
4. Add a test-only value in `PostgresBackedApiApplication.CreateConfiguration` for integration tests.
5. Never log secret values. Use structured logging with placeholders that exclude sensitive fields.

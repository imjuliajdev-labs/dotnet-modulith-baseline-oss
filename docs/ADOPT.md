# Adopting This Baseline For Your Own Project

This guide covers adopting the baseline into your own product repo, renaming it, and removing the reference modules so you start clean with only the governed platform and your own modules. Whether you begin from a GitHub fork or a local clone of the OSS repo, the supported adoption flow runs from a local clone of the baseline into a separate target folder for your product.

> Recommended for first-time human adopters: `pwsh ./scripts/Start-Adoption.ps1`
>
> Advanced path for automation, agents, or already-decided inputs: `templates/adoption/adopt-spec.example.json` + `pwsh ./scripts/Adopt-Baseline.ps1`

## Before You Start

The safest mental model is:

- **source baseline repo** = your local clone of this baseline
- **target product repo** = the separate local folder where your adopted project will be created

Example:

- source baseline repo: `C:\repos\dotnet-modulith-baseline-oss`
- target product repo: `C:\repos\my-product`

Important boundaries:

- do **not** run adoption from inside the target folder
- do **not** adopt in place inside the baseline repo when you are using the guided wrapper
- the target folder should ideally **not exist yet**; if it already exists, keep it **empty**
- cloning this repo locally does **not** send your edits back to the baseline repo; your changes stay local unless you explicitly push to a remote you control or have write access to

## Choose Your Path

### Path 1: Guided adoption for first-time humans

Use this when you have not fully resolved the target folder, rename, or module-retention decisions yet.

```powershell
pwsh ./scripts/Start-Adoption.ps1
```

The guided wrapper gathers the inputs, writes `adopt-spec.json` into the target repo, runs a dry run first, and only proceeds to apply mode after explicit confirmation.

### Path 2: Config-first adoption for automation, agents, and already-decided inputs

Use this when you already know the target folder, project name, technical slug, module preset, and validation posture.

```powershell
cp ./templates/adoption/adopt-spec.example.json ./adopt-spec.json
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json
```

This is the direct path for automation and agent-driven runs.

## Choosing Which Modules To Keep

`Platform` and `Identity` are core modules and must always stay. The remaining checked-in modules are teaching slices you may keep temporarily, study, or remove during adoption.

| If you want... | Keep |
| --- | --- |
| the cleanest product base | `Platform`, `Identity` |
| a realistic first product base | `Platform`, `Identity`, `SampleFeature`, `Blog` |
| all live teaching references | the full checked-in module set |
| a custom teaching mix | `custom` with an explicit `keepModules` array |

Use [`MODULE_GUIDE.md`](MODULE_GUIDE.md) when you want help deciding what each checked-in module is for before you choose.

## Recommended Path: Guided Adoption For First-Time Humans

Run this from the **source baseline repo root**:

```powershell
pwsh ./scripts/Start-Adoption.ps1
```

What the wrapper asks for:

- target folder path
- optional target git remote URL
- human-facing application name
- technical slug
- teaching-module posture or custom keep set
- validation profile
- whether the local Docker-backed database should be reset during apply mode

Key identity terms:

- `projectName` — the human-facing application or product name shown to users, such as the README title and browser title
- `projectSlug` — the technical identifier used for runtime identity strings such as the auth cookie name, Data Protection app name, migration advisory lock, telemetry service name, default solution file name, and default web package name
- `solutionFileName` — the `.sln` file name; by default this is derived from `projectSlug`

What the wrapper does:

- clones the current baseline into the target folder
- aligns that target with the current working tree snapshot
- writes `adopt-spec.json` into the target repo
- configures remotes safely
- runs `pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun` before any apply-mode mutation
- asks for explicit confirmation before it runs apply mode

During the module-selection step it also points you to [`MODULE_GUIDE.md`](MODULE_GUIDE.md) and this guide for more detail before you decide what to keep.

## Advanced Path: Config-First Adoption

Use this path when you already know the inputs or want the deterministic direct path for automation and agent-driven runs.

### Minimal example

```powershell
cp ./templates/adoption/adopt-spec.example.json ./adopt-spec.json
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json
```

### Supported module presets

| `modulePreset` | Keep set |
| --- | --- |
| `clean-base` | `Platform`, `Identity` |
| `recommended-first-product-base` | `Platform`, `Identity`, `SampleFeature`, `Blog` |
| `full-teaching-set` | full checked-in module set |
| `custom` | explicit `keepModules` array |

Current apply support:

- all four `modulePreset` values are supported in apply mode, including `custom` with an explicit `keepModules` array
- `Platform` and `Identity` must always be in the resolved keep set
- any combination of `SampleFeature`, `Blog`, `KnowledgeBase`, and `Admin` is valid
- the shared-runtime behavior test suite is regenerated from the resolved keep set, so adopted forks only keep the behavior assertions that still have backing modules

### Validation profiles

- `none` — skip validation entirely
- `fast` — run a reviewed manifest-backed subset through `pwsh ./scripts/Invoke-LocalGates.ps1 -Only <gate-id>` (`spec-governance`, `dependency-policy`, `build`, `backend-unit-tests`, `architecture-tests`, `frontend-lint`, `frontend-typecheck`, and `frontend-build`)
- `full` — run the canonical local gate wall through `pwsh ./scripts/Invoke-LocalGates.ps1`, preserving manifest order and each gate's `skipLocally` default

When you need a narrower rerun after a fix, use `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` instead of hand-rolling raw `dotnet test` or `pnpm` command lists.

### Spec notes

- the workflow validates the adoption spec shape before apply mode
- the example spec defaults scratch output to repo-local `.tmp`
- repo-local scratch roots must stay under the repo, and `os-temp` roots must stay under the operating-system temp directory
- the resolved scratch root is exported as `DOTNET_MODULITH_SCRATCH_ROOT` while validation runs so downstream governance scripts share the same scratch location

## What The Adoption Workflow Changes

Both supported entry points drive the same underlying adoption workflow. In apply mode it:

- resolves the selected module preset into concrete keep and remove sets
- derives the functional identity values that must be renamed from `projectSlug`
- applies the rename path for the baseline slug and solution identity
- removes unsupported teaching modules for the selected keep set
- regenerates shared-runtime behavior tests to match the resolved keep set
- optionally resets the local Docker-backed database when explicitly requested
- runs the requested validation profile unless `-SkipValidation` is supplied

## Identity Adoption Notes

The `Identity` module is the production-grade authentication and role-based authorization answer for this baseline. It is not a stub. See [`adr/ADR-IDENTITY-POSTURE.md`](adr/ADR-IDENTITY-POSTURE.md) for the governing decision.

### First-run bootstrap (Development)

1. Configure a Development-only seeded admin password through configuration. Either set `Modules:Identity:SeededAdmin:Password` in `appsettings.Development.json` or set the equivalent environment variable `Modules__Identity__SeededAdmin__Password`. Docker Compose uses `SEEDED_ADMIN_PASSWORD` in `.env` to feed the same setting. The password must be at least 12 characters and contain an uppercase letter, a lowercase letter, a digit, and a non-alphanumeric character.
2. Start the app and sign in as the seeded admin (`admin` by default unless you also configure `Modules:Identity:SeededAdmin:UserName`).
3. Create a real admin user through `POST /api/v1/identity/users`, assign the `Admin` role through `PUT /api/v1/identity/users/{actorId}/roles`, and sign out.
4. Sign in as the real admin user and verify you can still administer users and roles.
5. Remove or leave unset `Modules:Identity:SeededAdmin:*` before deploying to any non-development environment.

### Production deployment

Do **not** configure seeded admin or seeded machine credentials in Production, Staging, or any other non-Development / non-Testing environment. The app will fail fast at startup if seeded credentials are present outside the allowed environments. There is no override flag.

Provision real admin users through the user administration endpoints signed in as the initial admin created during first-run bootstrap.

### Adding a new role

1. Add the constant to `IdentityRoles` in `Identity.Infrastructure.Authentication`.
2. Seed the role in `IdentityDatabaseMigration` alongside the existing `Admin` / `User` / `Machine` seeds.
3. Update `IdentityAccountSupport.IsKnownRole` so validation accepts the new name.
4. Add `RoleRequirement` entries to the commands or queries that should be gated by the new role.

### Customizing the password policy

The password policy is configured in `Identity.Infrastructure.Authentication` when ASP.NET Core Identity is registered. Change the `PasswordOptions` there — `RequiredLength`, `RequireDigit`, `RequireLowercase`, `RequireUppercase`, `RequireNonAlphanumeric` — and update `IdentityBootstrapCredentialSafetyIntegrationTests` if you relax the minimum. The baseline ships with 12-character minimum length and all four character-class requirements enabled.

### Configuring cookie options for your domain

The cookie is `__Host-dotnet-modulith-baseline`, HttpOnly, Secure=Always, SameSite=Lax, 8h sliding. After the rename checklist below, the cookie name will carry your project identity. If your deployment requires a different SameSite mode (for example, because of a cross-subdomain frontend), change it in `IdentityInfrastructureServiceCollectionExtensions.cs` and add or update an ADR under `docs/adr/` describing why the relaxed posture is required. Do not relax `Secure` or `HttpOnly`.

### Configuring a seeded machine client (Development/Testing only)

Set:

- `Modules:Identity:SeededMachine:ApiKey` — the `{clientId}:{secret}` bootstrap value
- `Modules:Identity:SeededMachine:Roles` — the role list, typically `Machine`, optionally `Admin`

In production, create real machine clients through the machine-client administration endpoints and never ship seeded machine config.

## Manual/Reference Details

You usually do not need the rest of this document when you follow one of the supported scripts. These sections are the detailed reference for what the scripts automate and the fallback manual path if you need it.

### Rename Checklist

The project name `dotnet-modulith-baseline` appears in functional code, not just documentation. Replace all occurrences with your project name before deploying.

#### Critical (Functional Impact)

These affect runtime behavior and must be renamed:

| What | File | Line / Key | Why It Matters |
| --- | --- | --- | --- |
| Auth cookie name | `src/Modules/Identity/Identity.Infrastructure/IdentityInfrastructureServiceCollectionExtensions.cs` | `options.Cookie.Name = "__Host-dotnet-modulith-baseline"` | Browsers store sessions under this name |
| Data Protection app name | `src/BuildingBlocks/Infrastructure/Persistence/SharedRuntimePersistenceDefaults.cs` | `DataProtectionApplicationName = "dotnet-modulith-baseline"` | Browser auth cookies must use the same app discriminator across instances and deployments |
| Migration advisory lock | `src/BuildingBlocks/Infrastructure/Persistence/DatabaseMigrationRunner.cs` | `PostgresMigrationLockName = "dotnet-modulith-baseline:migrations"` | Prevents concurrent migration runs; collides if two baselines share a PostgreSQL instance |
| OpenTelemetry service name | `docker-compose.yml` | `OpenTelemetry__ServiceName: "dotnet-modulith-baseline-api"` | Identifies traces and metrics in your collector on the default local composed path |

#### Project structure

| What | File | Action |
| --- | --- | --- |
| Solution file | `dotnet-modulith-baseline.sln` | Rename file to `{your-project}.sln` |
| npm package name | `web/package.json` | Change `"name"` field |

#### Container builds

The supported adoption script now rewrites the checked-in Docker build inputs when it renames the solution file. If you are following the fallback manual path instead of the script, update these too:

- `Dockerfile` (`COPY dotnet-modulith-baseline.sln ./`)
- `Dockerfile.migrator` (`COPY dotnet-modulith-baseline.sln ./`)

#### Tests

After renaming the cookie, update the cookie name string in all test files that reference it. A global find-and-replace for `__Host-dotnet-modulith-baseline` covers these:

- `tests/Integration.Tests/**/*.cs` (roughly two dozen occurrences across auth, module-state, and module-specific test files)
- `web/tests/e2e/app-shell.spec.ts` (1 occurrence in the E2E cookie mock)

#### Documentation and schemas

These are cosmetic but should match your project identity:

- `README.md` title
- `web/index.html` title
- `social-preview.svg` title text
- `docs/TELEMETRY.md` service name default
- `.gitleaks.toml` repo comment
- `docs/schema/rule-enforcement-map.v1.schema.json` (`$id` field)
- `waivers/schema/waiver-file.v1.schema.json` (`title` field)
- `waivers/schema/waiver-entry.v1.schema.json` (`title` field)
- contributor-facing repo wording that still presents the fork as `dotnet-modulith-baseline`, especially `README.md`, `AGENTS.md`, and any baseline or ADR overview copy you expect downstream contributors or agents to read first

#### Scripts

Temporary directory names in scaffold test scripts use the project name as a prefix. These are cosmetic but worth renaming for clarity:

- `scripts/Test-ModuleScaffold.ps1`
- `scripts/Test-ModuleScaffoldFrontend.ps1`

### Removing Reference Modules

The reference modules (`SampleFeature`, `Blog`, `KnowledgeBase`, `Admin`) are teaching examples. You may want to remove some or all of them. `Platform` and `Identity` are core modules and cannot be removed.

#### Safe removal order

The teaching module dependency graph is directional. Among the four reference modules, only `Admin` reaches backward into other teaching modules:

1. **`Admin`** consumes `SampleFeature.PublicContracts` (events) and `KnowledgeBase.PublicContracts` (events plus a bounded shared-read example).
2. **`SampleFeature`**, **`Blog`**, and **`KnowledgeBase`** are fully standalone with respect to each other and to `Admin`. Each can be removed independently without breaking the other three.

Practical implications:

- removing `SampleFeature`, `Blog`, or `KnowledgeBase` only affects the matching tests, frontend feature, and the inverse `Admin` consumer for events that targeted that module
- removing `Admin` is the cleanest single-module removal because nothing else in the teaching set depends on it
- if your goal is a clean fork, remove all unwanted reference modules in one bounded pass

#### When to remove them

The reference modules are more than demo features. They are the checked-in teaching path for the current seams after scaffolding. Removing them does not remove the executable drift-protection model, but it does remove the live examples contributors and AI tools can study in the same repo.

Choose one of these paths explicitly:

1. **Clean base now:** If your top priority is a minimal product base, remove all unwanted teaching modules in one deliberate cleanup pass immediately after the rename.
2. **Scaffold-first, then remove:** If your top priority is keeping live examples in the fork while you prove your own first bounded context, scaffold and start one real module first, then remove the teaching modules in one cleanup pass once you no longer need those examples.

Avoid mixing partial module removal with new module creation over several small edits. Either keep the teaching set intact while you scaffold, or remove the unwanted modules in one bounded cleanup pass.

#### Steps per module

For each module you remove:

1. Remove the module folder under `src/Modules/{Module}/`.
2. Remove the project references from `dotnet-modulith-baseline.sln` (or your renamed `.sln`).
3. Remove the module's test files under `tests/Integration.Tests/Modules/{Module}/`.
4. Remove the module's unit test files under `tests/Module.UnitTests/{Module}/`.
5. Remove the module's architecture test files under `tests/Architecture.Tests/` if any are module-specific.
6. Remove the frontend feature folder under `web/src/features/{featureName}/` if the module has UI surface.
7. Remove any remaining cross-module references from other modules and tests.
8. Drop the module's PostgreSQL schema from your database using the module's actual schema name.

Notes:

- `src/ApiHost/ApiHost.csproj` and the main test projects use wildcard module references, so deleting the module folder removes those project references automatically
- `web/src/app/router/featureRegistry.tsx` auto-discovers `web/src/features/**/index.ts`, so deleting the feature folder is enough; no manual router edit is required
- the current reference-module schema names are `admin`, `sample_feature`, `blog`, and `knowledge_base`
- the only deliberate cross-teaching seams are `Admin -> SampleFeature.PublicContracts` (event consumer) and `Admin -> KnowledgeBase.PublicContracts` (event consumer plus shared-read example); removing `SampleFeature`, `Blog`, or `KnowledgeBase` only requires deleting the matching consumer in `Admin`, while removing `Admin` requires no companion cleanup in the other teaching modules

#### After removal

- run `pwsh ./scripts/Invoke-LocalGates.ps1` to exercise the canonical local validation wall
- if you only need to re-run one area after a fix, use `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` instead of dropping to a raw command list
- if you removed all three teaching modules, the app still boots with the core modules; the bootstrap manifest will contain `Platform` and `Identity`, and the frontend will still expose the `Platform` feature; your first scaffolded module will add additional module surface alongside those core modules

### Resetting The Database

After renaming or removing modules, reset your local database:

```powershell
# Drop and recreate (Docker)
docker compose down -v
docker compose up postgres -d

# Re-run migrations
$env:ConnectionStrings__BaselineDatabase = "Host=localhost;Database=baseline;Username=postgres;Password=postgres"
dotnet run --project src/Tools/DbMigrator -- --ConnectionStrings:BaselineDatabase="Host=localhost;Database=baseline;Username=postgres;Password=postgres"
```

Or simply drop the database and let the migrator recreate it:

```sql
DROP DATABASE baseline;
CREATE DATABASE baseline;
```

### Verifying Your Adopted Repo

After all rename and removal steps, run the full supported local validation wall to confirm everything is clean:

```powershell
pwsh ./scripts/Invoke-LocalGates.ps1
```

For targeted reruns after a fix, use the canonical gate dispatcher instead of reassembling the wall by hand. Common examples:

```powershell
pwsh ./scripts/Invoke-CiGate.ps1 -Id architecture-tests
pwsh ./scripts/Invoke-CiGate.ps1 -Id integration-tests
pwsh ./scripts/Invoke-CiGate.ps1 -Id contract-generation-and-compatibility
```

If a gate cannot run because of a missing prerequisite, report the exact missing prerequisite instead of silently skipping it. The most common cases are Docker/Testcontainers for `integration-tests` and `ConnectionStrings__BaselineDatabase` for `contract-generation-and-compatibility`.

All gates should pass. If any fail, the removal or rename missed a reference.

## Agent Note

Agent users should follow the same adoption workflow described above. The repo-owned helper prompt lives at [`../prompts/adopt-governed-baseline.md`](../prompts/adopt-governed-baseline.md). Keep this document as the canonical adoption checklist and update the prompt file in the same review whenever the adoption workflow changes.

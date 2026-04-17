# Adopting This Baseline For Your Own Project

This guide covers forking the baseline, renaming it, and removing the reference modules so you start clean with only the governed platform and your own modules.

## Guided First-Time Adoption Wrapper

If you are approaching the repo as a first-time human adopter and have not yet resolved the rename, target-folder, or module-retention inputs, start with the guided wrapper:

```powershell
pwsh ./scripts/Start-Adoption.ps1
```

What the wrapper does:

- asks for the target folder path and optional target git remote URL
- asks for the human-facing application name and derives a default technical slug you can override
- lets you choose a built-in teaching-module posture or make a custom keep/remove selection, with a short explanation of what each optional teaching module is for
- asks for the validation profile and whether the local Docker-backed database should be reset during apply mode
- clones the current baseline into the target folder, aligns that target with the current working tree snapshot, writes `adopt-spec.json` into the target repo, and configures remotes safely
- runs `pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun` in the target repo before any apply-mode mutation
- asks for explicit confirmation before it runs apply mode

The guided wrapper is a front-end to the same supported config-first adoption workflow below. It does not duplicate the rename or module-removal logic. During the module-selection step it also points the adopter to `docs/MODULE_GUIDE.md` and this guide for more detail before they decide what to keep.

Identity input terminology:

- `projectName` — the actual human-facing application or product name shown to users, such as the README title and browser title
- `projectSlug` — the technical identifier used for runtime identity strings such as the auth cookie name, Data Protection app name, migration advisory lock, telemetry service name, default solution file name, and default web package name
- `solutionFileName` — the `.sln` file name; by default this is derived from `projectSlug`

## Config-First Adoption Workflow

The repository also includes a config-driven direct workflow so you can make the core fork decisions explicitly and then apply the supported built-in fork shapes from one checked-in spec.

Start from the example spec:

```powershell
cp ./templates/adoption/adopt-spec.example.json ./adopt-spec.json
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun
```

Then apply a supported built-in preset:

```powershell
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json
```

What the workflow does:

- validates the adoption spec shape
- resolves the selected module preset into the concrete keep and remove sets
- derives the functional identity values that must be renamed from `projectSlug`
- resolves the scratch root (repo-local `.tmp/` by default in the example spec), validates that repo-local roots stay under the repo and `os-temp` roots stay under the operating-system temp directory, and shares the resolved path with downstream governance scripts through `DOTNET_MODULITH_SCRATCH_ROOT` during validation
- applies the rename path for the baseline slug and solution identity
- removes unsupported teaching modules for the supported built-in presets
- optionally resets the local Docker-backed database when explicitly requested
- runs the requested validation profile unless `-SkipValidation` is supplied, dispatching through the canonical local gate runner instead of maintaining a second raw command list

Current apply support:

- all four `modulePreset` values are supported in apply mode, including `custom` with an explicit `keepModules` array
- `Platform` and `Identity` must always be in the resolved keep set; any combination of `SampleFeature`, `Blog`, `KnowledgeBase`, and `Admin` is valid
- the shared-runtime behavior test suite is regenerated from the resolved keep set, so adopted forks only keep the behavior assertions that still have backing modules

Validation profile semantics:

- `none` — skip validation entirely
- `fast` — run a reviewed manifest-backed subset through `pwsh ./scripts/Invoke-LocalGates.ps1 -Only <gate-id>` (`spec-governance`, `dependency-policy`, `build`, `backend-unit-tests`, `architecture-tests`, `frontend-lint`, `frontend-typecheck`, and `frontend-build`)
- `full` — run the canonical local gate wall through `pwsh ./scripts/Invoke-LocalGates.ps1`, preserving manifest order and each gate's `skipLocally` default

When you need a narrower rerun after a fix, use `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` instead of hand-rolling raw `dotnet test` or `pnpm` command lists.

The resolved scratch root is also exported as `DOTNET_MODULITH_SCRATCH_ROOT` while the requested validation profile runs so downstream governance scripts keep their scratch output co-located with the adoption workflow.

Supported module presets in the example schema:

- `clean-base` — keep only `Platform` and `Identity`
- `recommended-first-product-base` — keep `Platform`, `Identity`, `SampleFeature`, and `Blog`
- `full-teaching-set` — keep the full checked-in module set
- `custom` — specify `keepModules` explicitly

The rename/removal checklist below remains the detailed reference for what the script is doing and the fallback manual path if you need a shape that apply mode does not support yet.

## Optional: AI-Assisted Adoption Prompt

This section is optional. The human-first path in [`../README.md`](../README.md), the guided wrapper `scripts/Start-Adoption.ps1`, and the rest of this guide remain the supported way to install and adopt the baseline.

If you use AI tools and want a clean first-pass validation of the adopter experience, choose one of these supported entry points after opening the repository root:

1. Prompt A: install and evaluate the OSS as-is (inline below)
2. Prompt B: fork, rename, and adopt it as a clean product base via the shared repo-owned prompt at [`../prompts/adopt-governed-baseline.md`](../prompts/adopt-governed-baseline.md)
3. Human-first guided wrapper: `pwsh ./scripts/Start-Adoption.ps1`

### Prompt A: Install And Evaluate As-Is

```text
You are in the root of the dotnet-modulith-baseline repository. Your job is to install, run, smoke-test, and explain how to use this OSS as a first-time adopter.

Work autonomously. Do not stop at planning. Prefer the supported Docker path first, and only fall back to the local path if Docker is unavailable or fails for an environment-specific reason.

Before doing anything else, read these files in this order:
1. AGENTS.md
2. README.md
3. docs/BASELINE_DECLARATION.md
4. docs/ADOPT.md

Goals:
1. Get the OSS running through the supported path.
2. Verify the app is actually usable, not just that commands exit successfully.
3. Give a concise “how to use it” walkthrough for a new adopter.
4. Classify any blocker as environment issue, documentation mismatch, or repo defect.

Execution rules:
- Prefer Docker quick start first.
- Do not edit repository files unless a real blocker clearly requires a fix.
- If something fails, diagnose the root cause before giving up.
- Do not ask for permission for obvious next steps.
- Keep going until you either verify the OSS works or you find a real blocker.
- Clean up any long-running containers or processes when you are done unless leaving them running is necessary for the final verification you report.

Step 1: Check prerequisites relevant to the chosen path.
For Docker path:
- Verify Docker and Docker Compose are available.

If Docker path is not viable, verify local prerequisites:
- .NET 10 SDK
- Node.js 20+
- PowerShell 7+
- pnpm via corepack
- PostgreSQL 16+
- mkcert only if needed for Vite HTTPS dev flow

Step 2: Prefer the full Docker composed path documented in the README.
- Run:
	- cp .env.example .env
	- docker compose up --build
- Wait for postgres, db-migrator, and api to be ready.
- Verify the app responds at http://localhost:8080

Step 3: Smoke-test the running app.
- Confirm the login page loads.
- Use the seeded browser credentials from the Development configuration. On the default checked-in Docker path, `.env.example` provides:
	- username: admin
	- password: LocalOnly!123
- Confirm login works if browser tooling is available.
- Verify representative endpoints if feasible:
	- /_host/status
	- /api/v1/platform/bootstrap
	- /api/v1/identity/antiforgery
- If feasible, test a machine-authenticated request using the seeded Development credential. On the default checked-in Docker path:
	- header: X-Machine-Key
	- value: MachineOnly!123

Step 4: If Docker is unavailable or blocked, use the supported local fallback from the README.
- Install frontend dependencies from web:
	- corepack enable
	- pnpm install
- For the browser shell on the local path, either:
	- run `pnpm dev` and browse to https://localhost:3000, or
	- run `pnpm build` once so ApiHost can serve the compiled `web/dist` assets
- Set:
	- ASPNETCORE_ENVIRONMENT=Development
	- ConnectionStrings__BaselineDatabase
	- Modules__Identity__SeededAdmin__Password
- If you also want to exercise the default machine-auth smoke on the local path, set:
	- Modules__Identity__SeededMachine__ApiKey=MachineOnly!123
	- Modules__Identity__SeededMachine__Roles__0=Machine
- Run ./scripts/Start-ApiHost.ps1
- If you chose `pnpm dev`, use the Vite origin for the login page and the ApiHost origin for API probes.
- Use the same smoke-test expectations as above.
- If local prerequisites are missing, report the exact missing prerequisite and stop only after checking whether a documented fallback exists.

Step 5: Explain how to use the OSS as a first-time adopter.
Give a concise walkthrough covering:
- what the user should see after login
- what Platform is for
- what Admin is for
- what SampleFeature is for
- what KnowledgeBase is for
- the safest next step if someone wants to fork this into their own project
- any important adopter caveats from docs/ADOPT.md

Step 6: Final deliverable.
Provide:
- the exact commands you ran
- what passed
- what did not pass
- any blocker with root cause
- whether this OSS appears ready for a first-time adopter to install and use
- whether it appears ready to fork as a base project

If you discover a docs mismatch or repo defect, cite the exact file and describe the impact clearly.
```

### Prompt B: Shared adoption prompt file

If you are using an agent in this repo, prefer the shared repo-owned adoption prompt at [`../prompts/adopt-governed-baseline.md`](../prompts/adopt-governed-baseline.md).

That prompt is intentionally a thin wrapper around this guide, the guided wrapper `scripts/Start-Adoption.ps1`, the example adoption spec, and `scripts/Adopt-Baseline.ps1`. Keep this document as the canonical adoption checklist and update the repo-owned prompt file in the same review whenever the adoption workflow changes.

## Identity Adoption

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

## Rename Checklist

The project name `dotnet-modulith-baseline` appears in functional code, not just documentation. Replace all occurrences with your project name before deploying.

### Critical (Functional Impact)

These affect runtime behavior and must be renamed:

| What                         | File                                                                                           | Line / Key                                                                  | Why It Matters                                                                           |
| ---------------------------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Auth cookie name             | `src/Modules/Identity/Identity.Infrastructure/IdentityInfrastructureServiceCollectionExtensions.cs` | `options.Cookie.Name = "__Host-dotnet-modulith-baseline"`                    | Browsers store sessions under this name                                                  |
| Data Protection app name     | `src/BuildingBlocks/Infrastructure/Persistence/SharedRuntimePersistenceDefaults.cs`            | `DataProtectionApplicationName = "dotnet-modulith-baseline"`                 | Browser auth cookies must use the same app discriminator across instances and deployments |
| Migration advisory lock      | `src/BuildingBlocks/Infrastructure/Persistence/DatabaseMigrationRunner.cs`                     | `PostgresMigrationLockName = "dotnet-modulith-baseline:migrations"`          | Prevents concurrent migration runs; collides if two baselines share a PostgreSQL instance |
| OpenTelemetry service name   | `docker-compose.yml`                                                                            | `OpenTelemetry__ServiceName: "dotnet-modulith-baseline-api"`                | Identifies traces and metrics in your collector on the default local composed path       |

### Project Structure

| What             | File                          | Action                              |
| ---------------- | ----------------------------- | ----------------------------------- |
| Solution file    | `dotnet-modulith-baseline.sln` | Rename file to `{your-project}.sln` |
| npm package name | `web/package.json`            | Change `"name"` field               |

### Container Builds

The supported adoption script now rewrites the checked-in Docker build inputs when it renames the solution file. If you are following the fallback manual path instead of the script, update these too:

- `Dockerfile` (`COPY dotnet-modulith-baseline.sln ./`)
- `Dockerfile.migrator` (`COPY dotnet-modulith-baseline.sln ./`)

### Tests

After renaming the cookie, update the cookie name string in all test files that reference it. A global find-and-replace for `__Host-dotnet-modulith-baseline` covers these:

- `tests/Integration.Tests/**/*.cs` (roughly two dozen occurrences across auth, module-state, and module-specific test files)
- `web/tests/e2e/app-shell.spec.ts` (1 occurrence in the E2E cookie mock)

### Documentation and Schemas

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

### Scripts

Temporary directory names in scaffold test scripts use the project name as a prefix. These are cosmetic but worth renaming for clarity:

- `scripts/Test-ModuleScaffold.ps1`
- `scripts/Test-ModuleScaffoldFrontend.ps1`

## Removing Reference Modules

The reference modules (`SampleFeature`, `Blog`, `KnowledgeBase`, `Admin`) are teaching examples. You may want to remove some or all of them. `Platform` and `Identity` are core modules and cannot be removed.

### Safe Removal Order

The teaching module dependency graph is directional. Among the four reference modules, only `Admin` reaches backward into other teaching modules:

1. **`Admin`** consumes `SampleFeature.PublicContracts` (events) and `KnowledgeBase.PublicContracts` (events plus a bounded shared-read example).
2. **`SampleFeature`**, **`Blog`**, and **`KnowledgeBase`** are fully standalone with respect to each other and to `Admin`. Each can be removed independently without breaking the other three.

Practical implications:

- Removing `SampleFeature`, `Blog`, or `KnowledgeBase` only affects the matching tests, frontend feature, and the inverse `Admin` consumer for events that targeted that module.
- Removing `Admin` is the cleanest single-module removal because nothing else in the teaching set depends on it.
- If your goal is a clean fork, remove all unwanted reference modules in one bounded pass.

### When To Remove Them

The reference modules are more than demo features. They are the checked-in teaching path for the current seams after scaffolding. Removing them does not remove the executable drift-protection model, but it does remove the live examples contributors and AI tools can study in the same repo.

Choose one of these paths explicitly:

1. **Clean base now:** If your top priority is a minimal product base, remove all unwanted teaching modules in one deliberate cleanup pass immediately after the rename.
2. **Scaffold-first, then remove:** If your top priority is keeping live examples in the fork while you prove your own first bounded context, scaffold and start one real module first, then remove the teaching modules in one cleanup pass once you no longer need those examples.

Avoid mixing partial module removal with new module creation over several small edits. Either keep the teaching set intact while you scaffold, or remove the unwanted modules in one bounded cleanup pass.

### Steps Per Module

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

- `src/ApiHost/ApiHost.csproj` and the main test projects use wildcard module references, so deleting the module folder removes those project references automatically.
- `web/src/app/router/featureRegistry.tsx` auto-discovers `web/src/features/**/index.ts`, so deleting the feature folder is enough. No manual router edit is required.
- The current reference-module schema names are `admin`, `sample_feature`, `blog`, and `knowledge_base`.
- The only deliberate cross-teaching seams are `Admin -> SampleFeature.PublicContracts` (event consumer) and `Admin -> KnowledgeBase.PublicContracts` (event consumer plus shared-read example). Removing `SampleFeature`, `Blog`, or `KnowledgeBase` only requires deleting the matching consumer in `Admin`; removing `Admin` requires no companion cleanup in the other teaching modules.

### After Removal

- Run `pwsh ./scripts/Invoke-LocalGates.ps1` to exercise the canonical local validation wall.
- If you only need to re-run one area after a fix, use `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` instead of dropping to a raw command list.
- If you removed all three teaching modules, the app still boots with the core modules. The bootstrap manifest will contain `Platform` and `Identity`, and the frontend will still expose the `Platform` feature. Your first scaffolded module will add additional module surface alongside those core modules.

## Resetting the Database

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

## Verifying Your Fork

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

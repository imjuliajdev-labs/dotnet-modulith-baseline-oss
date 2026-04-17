# Handler coverage report

BP-031 treats two coverage modes as first-class:

1. a direct `<HandlerName>Tests` class in a backend test project, or
2. a governed handler-coverage inventory entry that names the integration test class or classes covering the handler and explains why integration-level coverage is sufficient.

The inventory keeps the exception surface explicit instead of burying it in comments, and the generated artifact makes the split reviewable without reproducing the scan locally.

## Where the report lives

Running the Architecture test project regenerates `artifacts/handler-coverage.json` at the repository root as a local/CI-generated artifact. The file is gitignored and is not intended to be committed. The artifact has the shape:

```json
{
  "generatedAt": "2026-04-12T00:00:00.0000000+00:00",
  "totalHandlers": 0,
  "tested": ["HandlerName", "..."],
  "exempted": [
    {
      "handler": "HandlerName",
      "reason": "...",
      "coveringTests": ["IntegrationTestClassName"],
      "trackedBy": null
    }
  ]
}
```

- `tested` contains handlers covered by matching direct test classes.
- `exempted` contains handlers covered through the governed inventory.
- sorted arrays keep handler and exemption ordering stable across runs; `generatedAt` records the refresh time.

The artifact is produced by the `Emits_handler_coverage_artifact` test in `tests/Architecture.Tests/HandlerTestCoverageGuardrailTests.cs`.

## How to run the report locally

```bash
pwsh scripts/Report-HandlerCoverage.ps1
```

The script runs the handler-coverage guardrail tests, refreshes `artifacts/handler-coverage.json`, and prints a short summary.

## CI publication

The `architecture-tests` GitHub Actions job uploads `artifacts/handler-coverage.json` as the `handler-coverage` workflow artifact. Reviewers should be able to download that artifact directly from a PR or push run without regenerating it locally, and without relying on a committed copy in the repository.

## How to propose a new inventory entry

1. Edit `tests/Architecture.Tests/Support/HandlerCoverageInventory.cs` and add a new `HandlerCoverageExemption` entry in the appropriate section.
2. Include a clear justification and the exact integration test class name or names that cover the handler.
3. Run `pwsh scripts/Report-HandlerCoverage.ps1` if you want to inspect the refreshed local artifact or verify the CI artifact shape locally. Do not commit `artifacts/handler-coverage.json`.
4. In the PR description, explain why integration-level coverage is sufficient and link the covering test.
5. When a true direct handler test lands, remove the inventory entry in the same PR.

## Guardrails

The handler-coverage architecture tests enforce that:

- handlers missing both coverage modes fail the build
- every named covering integration test class actually exists under `tests/Integration.Tests/`
- every cited test class structurally references the handler — its message type, its class name, or imports the handler's containing namespace as a dispatch-coverage marker — after stripping comments and string literals, so a name-only mention in prose does not satisfy the rule. The reachability analyzer lives at `tests/Architecture.Tests/Support/HandlerDispatchReachability.cs`.
- stale inventory entries are rejected
- the CI workflow keeps publishing the artifact for review visibility

## Dispatch-coverage markers

Integration tests in this codebase exercise handlers through the HTTP endpoint pipeline rather than by constructing the dispatched message type directly. The dispatch reachability fact accepts a `using` directive importing the handler's containing application namespace as a structural coverage marker. By convention, that import carries an inline `// BP-031 dispatch-coverage marker` comment so future readers see the intent. Renaming a handler module breaks the build until the marker is updated, which keeps the inventory honest under refactoring.

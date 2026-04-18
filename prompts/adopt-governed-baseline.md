# Adopt Governed Baseline

Use this prompt when forking, renaming, or adopting this baseline into a new product base.

This prompt follows the same supported adoption workflow documented in [docs/ADOPT.md](../docs/ADOPT.md). It is not a parallel path.

- Read [AGENTS.md](../AGENTS.md) first, then follow its required governed-doc read order.
- Read [README.md](../README.md), [docs/BASELINE_DECLARATION.md](../docs/BASELINE_DECLARATION.md), [docs/BASELINE_OVERVIEW.md](../docs/BASELINE_OVERVIEW.md), [docs/MODULE_GUIDE.md](../docs/MODULE_GUIDE.md), and [docs/ADOPT.md](../docs/ADOPT.md).
- Prefer the two supported entry points exactly as documented in `ADOPT.md`:
  - first-time human adopter with unresolved inputs -> `pwsh ./scripts/Start-Adoption.ps1`
  - agent-driven or already-decided adoption -> [templates/adoption/adopt-spec.example.json](../templates/adoption/adopt-spec.example.json) plus `pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun` before apply mode
- Keep `Platform` and `Identity`.
- Do not invent a parallel rename, module-removal, or validation workflow when [docs/ADOPT.md](../docs/ADOPT.md) already covers the supported path.
- If inputs must be gathered manually, resolve the target path, optional target remote, the human-facing application or product name, the technical slug, optional `solutionFileName`, module preset / keep set, validation profile, and whether to reset the local database.
- When the adopter is deciding which teaching modules to keep, point them to [docs/MODULE_GUIDE.md](../docs/MODULE_GUIDE.md) and make the decision explicit across `SampleFeature`, `Blog`, `KnowledgeBase`, and `Admin`.
- The target product folder should be separate from the source baseline repo and should ideally not exist yet; if it already exists, keep it empty.
- Let the supported adoption workflow drive validation through `pwsh ./scripts/Invoke-LocalGates.ps1`. Use `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` only for targeted reruns after a fix.
- Report the resolved inputs, whether the task is dry-run or apply mode, the keep/remove module set, any blocked prerequisites, and the next recommended action.

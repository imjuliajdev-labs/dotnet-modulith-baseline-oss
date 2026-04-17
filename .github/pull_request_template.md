## Summary

Describe the change and the problem it solves.

## Validation

- [ ] `pwsh ./scripts/Invoke-LocalGates.ps1` or targeted reruns via `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` as appropriate
- [ ] `pwsh ./scripts/Generate-Contracts.ps1` if browser-consumed HTTP contracts changed
- [ ] Any skipped local gates or missing prerequisites are called out explicitly

## Documentation

- [ ] README updated if setup, workflow, or supported scope changed
- [ ] Governed docs updated together if a governed rule or enforcement path changed
- [ ] Module-specific docs updated if a reference slice or module workflow changed

## Notes

Call out any architectural tradeoffs, temporary constraints, or follow-up work.
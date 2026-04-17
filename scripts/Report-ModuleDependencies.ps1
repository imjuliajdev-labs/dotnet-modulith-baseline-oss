$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

# Must stay in sync with:
#   tests/Architecture.Tests/CrossModuleReferenceCapGuardrailTests.cs
#   tests/Architecture.Tests/Support/ModuleDependencyReportInventory.cs
$repositoryRoot = Get-RepositoryRoot
$CrossModulePublicContractsReferenceCap = Get-CrossModulePublicContractsReferenceCap
$rows = Get-ModuleDependencyReportRows -RepositoryRoot $repositoryRoot

$moduleColumnWidth = [Math]::Max(('Module').Length, (@($rows | ForEach-Object { $_.Module.Length }) | Measure-Object -Maximum).Maximum)
$refsColumnWidth = [Math]::Max(('Refs').Length, 4)
$headroomColumnWidth = [Math]::Max(('Headroom').Length, 8)

$headerFormat = "{0,-$moduleColumnWidth}  {1,-$refsColumnWidth}  {2,-$headroomColumnWidth}  {3}"
Write-Host ([string]::Format($headerFormat, 'Module', 'Refs', 'Headroom', 'References'))
Write-Host ([string]::Format($headerFormat, ('-' * $moduleColumnWidth), ('-' * $refsColumnWidth), ('-' * $headroomColumnWidth), ('-' * 10)))

foreach ($row in $rows) {
    Write-Host ([string]::Format($headerFormat, $row.Module, $row.Refs, $row.Headroom, $row.References))
}

Write-Host ''
Write-Host "Cross-module PublicContracts reference cap: $CrossModulePublicContractsReferenceCap"

$violations = @($rows | Where-Object { $_.Headroom -le 0 })
if ($violations.Count -gt 0) {
    $violationNames = ($violations | ForEach-Object { "$($_.Module) (Refs=$($_.Refs), Headroom=$($_.Headroom))" }) -join '; '
    throw "One or more modules have no cross-module PublicContracts headroom: $violationNames"
}

Write-Host 'Module dependency report: all modules have headroom > 0.'

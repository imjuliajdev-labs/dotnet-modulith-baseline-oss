param(
    [string]$SpecFile
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$resolvedSpecFile = if ([string]::IsNullOrWhiteSpace($SpecFile)) {
    Join-Path -Path $repositoryRoot -ChildPath 'templates/module/module-spec.example.json'
}
else {
    $SpecFile
}

$scaffoldScript = Join-Path -Path $repositoryRoot -ChildPath 'scripts/New-Module.ps1'
$sourceWebRoot = Join-Path -Path $repositoryRoot -ChildPath 'web'

$spec = Read-JsonFileAsHashtable -Path $resolvedSpecFile
Test-ModuleSpec -Spec $spec

if (-not [bool]$spec['hasFrontendSurface']) {
    Write-Host 'Skipping scaffold frontend validation because the module spec does not declare frontend surface.'
    return
}

if (-not [bool]$spec['hasOperatorManagedSettings']) {
    Write-Host 'Skipping scaffold frontend validation because the module spec does not declare operator-managed settings.'
    return
}

function Copy-WebWorkspace {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRoot,

        [Parameter(Mandatory)]
        [string]$TargetRoot
    )

    Get-ChildItem -Path $SourceRoot -Recurse -File |
        Where-Object {
            $relativePath = Convert-ToRepoRelativePath -Root $SourceRoot -Path $_.FullName
            $relativePath -notmatch '^(node_modules|dist|test-results)/'
        } |
        ForEach-Object {
            $relativePath = Convert-ToRepoRelativePath -Root $SourceRoot -Path $_.FullName
            $destinationPath = Join-Path -Path $TargetRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
            $destinationDirectory = Split-Path -Path $destinationPath -Parent

            New-Item -Path $destinationDirectory -ItemType Directory -Force | Out-Null
            Copy-Item -Path $_.FullName -Destination $destinationPath -Force
        }
}

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)

    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

$temporaryRoot = New-GovernanceScratchDirectory -Purpose 'module-scaffold-frontend' -RepositoryRoot $repositoryRoot

try {
    Copy-WebWorkspace -SourceRoot $sourceWebRoot -TargetRoot (Join-Path -Path $temporaryRoot -ChildPath 'web')

    & $scaffoldScript -SpecFile $resolvedSpecFile -OutputRoot $temporaryRoot

    $webRoot = Join-Path -Path $temporaryRoot -ChildPath 'web'
    $generatedFeatureFile = "src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx"
    $generatedSettingsTagsFile = "src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsTags.ts"
    $generatedSettingsApiFile = "src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts"
    $generatedSettingsProjectionFile = "src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts"
    $generatedSettingsApiTestFile = "tests/unit/$($spec['moduleKey'])-settings-api.test.ts"
    $generatedSettingsTagsTestFile = "tests/unit/$($spec['moduleKey'])-settings-tags.test.ts"
    $generatedFeatureTestFile = "tests/unit/$($spec['moduleKey'])-feature.test.tsx"
    $generatedFeatureE2eFile = "tests/e2e/$($spec['moduleKey']).spec.ts"
    $temporaryPlaywrightConfigFile = Join-Path -Path $webRoot -ChildPath 'playwright.scaffold.config.ts'
    $previewPort = Get-FreeLoopbackPort

    foreach ($relativePath in @($generatedFeatureFile, $generatedSettingsTagsFile, $generatedSettingsApiFile, $generatedSettingsProjectionFile, $generatedSettingsApiTestFile, $generatedSettingsTagsTestFile, $generatedFeatureTestFile, $generatedFeatureE2eFile)) {
        $fullPath = Join-Path -Path $webRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -Path $fullPath -PathType Leaf)) {
            throw "Scaffold frontend validation expected generated file '$relativePath'."
        }
    }

        @'
import { defineConfig, devices } from '@playwright/test';

    const previewPort = __PREVIEW_PORT__;
    const previewUrl = 'https://127.0.0.1:' + previewPort;

export default defineConfig({
    testDir: './tests/e2e',
    fullyParallel: false,
    workers: 1,
    reporter: 'line',
    use: {
        baseURL: previewUrl,
        ignoreHTTPSErrors: true,
        trace: 'retain-on-failure',
    },
    webServer: {
        command: 'pwsh -NoLogo -Command "pnpm exec vite build --mode e2e; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; pnpm exec vite preview --host 127.0.0.1 --port __PREVIEW_PORT__ --strictPort"',
        env: {
            ...process.env,
            VITE_API_URL: '/',
            VITE_SIGNALR_URL: '/',
        },
        ignoreHTTPSErrors: true,
        reuseExistingServer: false,
        timeout: 240000,
        url: previewUrl,
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'] },
        },
    ],
});
'@.Replace('__PREVIEW_PORT__', [string]$previewPort) | Set-Content -Path $temporaryPlaywrightConfigFile

    Push-Location $webRoot
    try {
        corepack enable | Out-Null
        pnpm install --frozen-lockfile | Out-Null
        pnpm exec eslint $generatedFeatureFile $generatedSettingsTagsFile $generatedSettingsApiFile $generatedSettingsProjectionFile $generatedSettingsApiTestFile $generatedSettingsTagsTestFile $generatedFeatureTestFile $generatedFeatureE2eFile
        pnpm exec tsc --noEmit -p tsconfig.app.json
        pnpm exec vitest run --config vitest.config.ts $generatedSettingsApiTestFile $generatedSettingsTagsTestFile $generatedFeatureTestFile
        pnpm exec playwright install chromium | Out-Null
        pnpm exec playwright test --config playwright.scaffold.config.ts $generatedFeatureE2eFile
    }
    finally {
        Pop-Location
    }

    Write-Host 'Scaffold frontend validation passed against the generated feature anchors.'
}
finally {
    if (Test-Path -Path $temporaryRoot) {
        Remove-Item -Path $temporaryRoot -Recurse -Force
    }
}
param(
    [Parameter(Mandatory)]
    [string]$BaselinePath,

    [Parameter(Mandatory)]
    [string]$CandidatePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$httpMethods = @('get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace')

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-JsonHashtable {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "JSON file was not found: $Path"
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}

function Get-MapValue {
    param(
        [object]$Map,
        [string]$Key
    )

    if ($Map -is [System.Collections.IDictionary] -and $Map.Contains($Key)) {
        return $Map[$Key]
    }

    return $null
}

function Get-ObjectKeys {
    param(
        [object]$Map
    )

    if ($Map -is [System.Collections.IDictionary]) {
        return @($Map.Keys)
    }

    return @()
}

function Get-ArrayValue {
    param(
        [object]$Value
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Collections.IDictionary] -or $Value -is [string]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        return @($Value)
    }

    return @($Value)
}

function Test-ValueEquality {
    param(
        [object]$Left,
        [object]$Right
    )

    $leftIsCollection = $Left -is [System.Collections.IEnumerable] -and $Left -isnot [string] -and $Left -isnot [System.Collections.IDictionary]
    $rightIsCollection = $Right -is [System.Collections.IEnumerable] -and $Right -isnot [string] -and $Right -isnot [System.Collections.IDictionary]

    if ($leftIsCollection -or $rightIsCollection) {
        $leftValues = @(Get-ArrayValue -Value $Left)
        $rightValues = @(Get-ArrayValue -Value $Right)

        if ($leftValues.Count -ne $rightValues.Count) {
            return $false
        }

        for ($index = 0; $index -lt $leftValues.Count; $index++) {
            if ($leftValues[$index] -ne $rightValues[$index]) {
                return $false
            }
        }

        return $true
    }

    return $Left -eq $Right
}

function Get-RefName {
    param(
        [object]$Schema
    )

    $reference = Get-MapValue -Map $Schema -Key '$ref'
    if ($reference -is [string] -and $reference -match '^#/components/schemas/(?<name>[^/]+)$') {
        return $Matches['name']
    }

    return $null
}

function Resolve-Schema {
    param(
        [hashtable]$Document,
        [object]$Schema
    )

    $current = $Schema
    while ($true) {
        $refName = Get-RefName -Schema $current
        if ([string]::IsNullOrWhiteSpace($refName)) {
            return $current
        }

        $schemas = Get-MapValue -Map (Get-MapValue -Map $Document -Key 'components') -Key 'schemas'
        Assert-Condition ($schemas -is [System.Collections.IDictionary] -and $schemas.Contains($refName)) "Schema reference '#/components/schemas/$refName' could not be resolved."
        $current = $schemas[$refName]
    }
}

function Compare-ScalarValue {
    param(
        [object]$BaselineValue,
        [object]$CandidateValue,
        [string]$Path,
        [string]$FieldName
    )

    if ($null -eq $BaselineValue) {
        return
    }

    Assert-Condition (Test-ValueEquality -Left $BaselineValue -Right $CandidateValue) "Breaking OpenAPI change at ${Path}: field '$FieldName' changed from '$BaselineValue' to '$CandidateValue'."
}

function Compare-Schema {
    param(
        [hashtable]$BaselineDocument,
        [object]$BaselineSchema,
        [hashtable]$CandidateDocument,
        [object]$CandidateSchema,
        [string]$Path,
        [System.Collections.Generic.HashSet[string]]$VisitedRefs
    )

    if ($null -eq $BaselineSchema) {
        return
    }

    Assert-Condition ($null -ne $CandidateSchema) "Breaking OpenAPI change at ${Path}: schema is missing from candidate."

    $baselineRef = Get-RefName -Schema $BaselineSchema
    $candidateRef = Get-RefName -Schema $CandidateSchema
    if (-not [string]::IsNullOrWhiteSpace($baselineRef) -and -not [string]::IsNullOrWhiteSpace($candidateRef)) {
        $visitKey = "$baselineRef|$candidateRef"
        if ($VisitedRefs.Contains($visitKey)) {
            return
        }

        [void]$VisitedRefs.Add($visitKey)
    }

    $baselineResolved = Resolve-Schema -Document $BaselineDocument -Schema $BaselineSchema
    $candidateResolved = Resolve-Schema -Document $CandidateDocument -Schema $CandidateSchema

    Compare-ScalarValue -BaselineValue (Get-MapValue -Map $baselineResolved -Key 'type') -CandidateValue (Get-MapValue -Map $candidateResolved -Key 'type') -Path $Path -FieldName 'type'
    Compare-ScalarValue -BaselineValue (Get-MapValue -Map $baselineResolved -Key 'format') -CandidateValue (Get-MapValue -Map $candidateResolved -Key 'format') -Path $Path -FieldName 'format'

    $baselineEnum = Get-ArrayValue -Value (Get-MapValue -Map $baselineResolved -Key 'enum')
    $candidateEnum = Get-ArrayValue -Value (Get-MapValue -Map $candidateResolved -Key 'enum')
    foreach ($enumValue in $baselineEnum) {
        Assert-Condition ($candidateEnum -contains $enumValue) "Breaking OpenAPI change at ${Path}: enum value '$enumValue' was removed."
    }

    $baselineRequired = @((Get-ArrayValue -Value (Get-MapValue -Map $baselineResolved -Key 'required')) | ForEach-Object { [string]$_ })
    $candidateRequired = @((Get-ArrayValue -Value (Get-MapValue -Map $candidateResolved -Key 'required')) | ForEach-Object { [string]$_ })
    foreach ($requiredProperty in $baselineRequired) {
        Assert-Condition ($candidateRequired -contains $requiredProperty) "Breaking OpenAPI change at ${Path}: required property '$requiredProperty' is no longer required."
    }

    foreach ($requiredProperty in $candidateRequired) {
        Assert-Condition ($baselineRequired -contains $requiredProperty) "Breaking OpenAPI change at ${Path}: property '$requiredProperty' was added as required."
    }

    $baselineProperties = Get-MapValue -Map $baselineResolved -Key 'properties'
    $candidateProperties = Get-MapValue -Map $candidateResolved -Key 'properties'
    foreach ($propertyName in Get-ObjectKeys -Map $baselineProperties) {
        Assert-Condition ($candidateProperties -is [System.Collections.IDictionary] -and $candidateProperties.Contains($propertyName)) "Breaking OpenAPI change at ${Path}: property '$propertyName' was removed."
        Compare-Schema -BaselineDocument $BaselineDocument -BaselineSchema $baselineProperties[$propertyName] -CandidateDocument $CandidateDocument -CandidateSchema $candidateProperties[$propertyName] -Path "$Path.$propertyName" -VisitedRefs $VisitedRefs
    }

    $baselineItems = Get-MapValue -Map $baselineResolved -Key 'items'
    if ($null -ne $baselineItems) {
        Compare-Schema -BaselineDocument $BaselineDocument -BaselineSchema $baselineItems -CandidateDocument $CandidateDocument -CandidateSchema (Get-MapValue -Map $candidateResolved -Key 'items') -Path "$Path[]" -VisitedRefs $VisitedRefs
    }

    foreach ($compositionField in @('allOf', 'anyOf', 'oneOf')) {
        $baselineEntries = @(Get-ArrayValue -Value (Get-MapValue -Map $baselineResolved -Key $compositionField))
        if ($baselineEntries.Count -eq 0) {
            continue
        }

        $candidateEntries = @(Get-ArrayValue -Value (Get-MapValue -Map $candidateResolved -Key $compositionField))
        Assert-Condition ($candidateEntries.Count -ge $baselineEntries.Count) "Breaking OpenAPI change at ${Path}: '$compositionField' entries were removed."

        for ($index = 0; $index -lt $baselineEntries.Count; $index++) {
            Compare-Schema -BaselineDocument $BaselineDocument -BaselineSchema $baselineEntries[$index] -CandidateDocument $CandidateDocument -CandidateSchema $candidateEntries[$index] -Path "$Path.$compositionField[$index]" -VisitedRefs $VisitedRefs
        }
    }
}

function Compare-ContentMap {
    param(
        [hashtable]$BaselineDocument,
        [object]$BaselineContent,
        [hashtable]$CandidateDocument,
        [object]$CandidateContent,
        [string]$Path
    )

    foreach ($contentType in Get-ObjectKeys -Map $BaselineContent) {
        Assert-Condition ($CandidateContent -is [System.Collections.IDictionary] -and $CandidateContent.Contains($contentType)) "Breaking OpenAPI change at ${Path}: content type '$contentType' was removed."
        Compare-Schema -BaselineDocument $BaselineDocument -BaselineSchema (Get-MapValue -Map $BaselineContent[$contentType] -Key 'schema') -CandidateDocument $CandidateDocument -CandidateSchema (Get-MapValue -Map $CandidateContent[$contentType] -Key 'schema') -Path "$Path.$contentType" -VisitedRefs ([System.Collections.Generic.HashSet[string]]::new())
    }
}

function Compare-Parameters {
    param(
        [hashtable]$BaselineDocument,
        [object]$BaselineOperation,
        [hashtable]$CandidateDocument,
        [object]$CandidateOperation,
        [string]$Path
    )

    $candidateParameters = @{}
    foreach ($parameter in Get-ArrayValue -Value (Get-MapValue -Map $CandidateOperation -Key 'parameters')) {
        $key = '{0}:{1}' -f $parameter['in'], $parameter['name']
        $candidateParameters[$key] = $parameter
    }

    foreach ($parameter in Get-ArrayValue -Value (Get-MapValue -Map $BaselineOperation -Key 'parameters')) {
        $key = '{0}:{1}' -f $parameter['in'], $parameter['name']
        Assert-Condition ($candidateParameters.Contains($key)) "Breaking OpenAPI change at ${Path}: parameter '$key' was removed."

        $candidateParameter = $candidateParameters[$key]
        Compare-ScalarValue -BaselineValue $parameter['required'] -CandidateValue $candidateParameter['required'] -Path "$Path.parameter.$key" -FieldName 'required'
        Compare-Schema -BaselineDocument $BaselineDocument -BaselineSchema (Get-MapValue -Map $parameter -Key 'schema') -CandidateDocument $CandidateDocument -CandidateSchema (Get-MapValue -Map $candidateParameter -Key 'schema') -Path "$Path.parameter.$key" -VisitedRefs ([System.Collections.Generic.HashSet[string]]::new())
    }
}

function Compare-RequestBody {
    param(
        [hashtable]$BaselineDocument,
        [object]$BaselineOperation,
        [hashtable]$CandidateDocument,
        [object]$CandidateOperation,
        [string]$Path
    )

    $baselineRequestBody = Get-MapValue -Map $BaselineOperation -Key 'requestBody'
    if ($null -eq $baselineRequestBody) {
        return
    }

    $candidateRequestBody = Get-MapValue -Map $CandidateOperation -Key 'requestBody'
    Assert-Condition ($null -ne $candidateRequestBody) "Breaking OpenAPI change at ${Path}: request body was removed."

    $baselineRequired = [bool](Get-MapValue -Map $baselineRequestBody -Key 'required')
    $candidateRequired = [bool](Get-MapValue -Map $candidateRequestBody -Key 'required')
    if (-not $baselineRequired -and $candidateRequired) {
        throw "Breaking OpenAPI change at ${Path}: request body changed from optional to required."
    }

    Compare-ContentMap -BaselineDocument $BaselineDocument -BaselineContent (Get-MapValue -Map $baselineRequestBody -Key 'content') -CandidateDocument $CandidateDocument -CandidateContent (Get-MapValue -Map $candidateRequestBody -Key 'content') -Path "$Path.requestBody"
}

function Compare-Responses {
    param(
        [hashtable]$BaselineDocument,
        [object]$BaselineOperation,
        [hashtable]$CandidateDocument,
        [object]$CandidateOperation,
        [string]$Path
    )

    $baselineResponses = Get-MapValue -Map $BaselineOperation -Key 'responses'
    $candidateResponses = Get-MapValue -Map $CandidateOperation -Key 'responses'

    foreach ($statusCode in Get-ObjectKeys -Map $baselineResponses) {
        Assert-Condition ($candidateResponses -is [System.Collections.IDictionary] -and $candidateResponses.Contains($statusCode)) "Breaking OpenAPI change at ${Path}: response '$statusCode' was removed."
        Compare-ContentMap -BaselineDocument $BaselineDocument -BaselineContent (Get-MapValue -Map $baselineResponses[$statusCode] -Key 'content') -CandidateDocument $CandidateDocument -CandidateContent (Get-MapValue -Map $candidateResponses[$statusCode] -Key 'content') -Path "$Path.response.$statusCode"
    }
}

function Compare-Operation {
    param(
        [hashtable]$BaselineDocument,
        [string]$PathKey,
        [string]$Method,
        [object]$BaselineOperation,
        [hashtable]$CandidateDocument,
        [object]$CandidateOperation
    )

    $operationPath = "$Method $PathKey"
    Compare-ScalarValue -BaselineValue (Get-MapValue -Map $BaselineOperation -Key 'operationId') -CandidateValue (Get-MapValue -Map $CandidateOperation -Key 'operationId') -Path $operationPath -FieldName 'operationId'
    Compare-Parameters -BaselineDocument $BaselineDocument -BaselineOperation $BaselineOperation -CandidateDocument $CandidateDocument -CandidateOperation $CandidateOperation -Path $operationPath
    Compare-RequestBody -BaselineDocument $BaselineDocument -BaselineOperation $BaselineOperation -CandidateDocument $CandidateDocument -CandidateOperation $CandidateOperation -Path $operationPath
    Compare-Responses -BaselineDocument $BaselineDocument -BaselineOperation $BaselineOperation -CandidateDocument $CandidateDocument -CandidateOperation $CandidateOperation -Path $operationPath
}

function Test-ProblemDetailsResponses {
    param(
        [hashtable]$Document
    )

    $paths = Get-MapValue -Map $Document -Key 'paths'
    foreach ($pathKey in Get-ObjectKeys -Map $paths) {
        $pathItem = $paths[$pathKey]
        foreach ($method in $httpMethods) {
            if (-not ($pathItem -is [System.Collections.IDictionary] -and $pathItem.Contains($method))) {
                continue
            }

            $responses = Get-MapValue -Map $pathItem[$method] -Key 'responses'
            foreach ($statusCode in Get-ObjectKeys -Map $responses) {
                $numericStatusCode = 0
                $isNumeric = [int]::TryParse([string]$statusCode, [ref]$numericStatusCode)
                if ($isNumeric -and $numericStatusCode -ge 200 -and $numericStatusCode -lt 400) {
                    continue
                }

                $problemContent = Get-MapValue -Map (Get-MapValue -Map $responses[$statusCode] -Key 'content') -Key 'application/problem+json'
                Assert-Condition ($null -ne $problemContent) "OpenAPI contract is invalid at $method $pathKey response ${statusCode}: application/problem+json content is required."

                $schemaRef = Get-MapValue -Map (Get-MapValue -Map $problemContent -Key 'schema') -Key '$ref'
                Assert-Condition ($schemaRef -eq '#/components/schemas/ProblemDetails') "OpenAPI contract is invalid at $method $pathKey response ${statusCode}: application/problem+json must reference '#/components/schemas/ProblemDetails'."
            }
        }
    }
}

function Test-VersionSetMetadata {
    param(
        [hashtable]$Document,
        [string]$ExpectedVersionSet
    )

    Assert-Condition ((Get-MapValue -Map $Document -Key 'x-baseline-version-set') -eq $ExpectedVersionSet) "OpenAPI contract is invalid: document root must declare x-baseline-version-set='$ExpectedVersionSet'."

    $paths = Get-MapValue -Map $Document -Key 'paths'
    foreach ($pathKey in Get-ObjectKeys -Map $paths) {
        $pathItem = $paths[$pathKey]
        foreach ($method in $httpMethods) {
            if (-not ($pathItem -is [System.Collections.IDictionary] -and $pathItem.Contains($method))) {
                continue
            }

            $versionSet = Get-MapValue -Map $pathItem[$method] -Key 'x-baseline-version-set'
            Assert-Condition ($versionSet -eq $ExpectedVersionSet) "OpenAPI contract is invalid at $method ${pathKey}: x-baseline-version-set must equal '${ExpectedVersionSet}'."
        }
    }
}

$baseline = Read-JsonHashtable -Path $BaselinePath
$candidate = Read-JsonHashtable -Path $CandidatePath

$baselinePaths = Get-MapValue -Map $baseline -Key 'paths'
$candidatePaths = Get-MapValue -Map $candidate -Key 'paths'

foreach ($pathKey in Get-ObjectKeys -Map $baselinePaths) {
    Assert-Condition ($candidatePaths -is [System.Collections.IDictionary] -and $candidatePaths.Contains($pathKey)) "Breaking OpenAPI change: path '$pathKey' was removed."
    $baselinePathItem = $baselinePaths[$pathKey]
    $candidatePathItem = $candidatePaths[$pathKey]

    foreach ($method in $httpMethods) {
        if (-not ($baselinePathItem -is [System.Collections.IDictionary] -and $baselinePathItem.Contains($method))) {
            continue
        }

        Assert-Condition ($candidatePathItem -is [System.Collections.IDictionary] -and $candidatePathItem.Contains($method)) "Breaking OpenAPI change: operation '$method $pathKey' was removed."
        Compare-Operation -BaselineDocument $baseline -PathKey $pathKey -Method $method -BaselineOperation $baselinePathItem[$method] -CandidateDocument $candidate -CandidateOperation $candidatePathItem[$method]
    }
}

Test-ProblemDetailsResponses -Document $candidate
Test-VersionSetMetadata -Document $candidate -ExpectedVersionSet 'v1'

Write-Host 'Validated OpenAPI compatibility and problem-response metadata.'
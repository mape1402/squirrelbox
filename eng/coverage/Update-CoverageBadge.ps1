param(
    [string] $CoverageRoot = "TestResults",
    [string] $BadgePath = "assets/coverage-badge.json",
    [decimal] $MinimumLineCoverage = 99,
    [decimal] $MinimumBranchCoverage = 99
)

$ErrorActionPreference = "Stop"

$files = Get-ChildItem -Path $CoverageRoot -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue
if (-not $files) {
    throw "No Cobertura coverage files were found under '$CoverageRoot'."
}

$lineHitsBySourceLine = @{}
$branchTotalsBySourceLine = @{}
$branchCoveredBySourceLine = @{}

foreach ($file in $files) {
    [xml] $document = Get-Content -LiteralPath $file.FullName
    foreach ($class in $document.coverage.packages.package.classes.class) {
        $sourceFile = [string] $class.filename
        foreach ($line in @($class.lines.line)) {
            if ($null -eq $line -or [string]::IsNullOrWhiteSpace([string] $line.number)) {
                continue
            }

            $sourceLineKey = "$sourceFile`:$($line.number)"
            if (-not $lineHitsBySourceLine.ContainsKey($sourceLineKey)) {
                $lineHitsBySourceLine[$sourceLineKey] = 0
            }

            $lineHitsBySourceLine[$sourceLineKey] += [int] $line.hits

            if ($line.branch -eq "true" -and $line."condition-coverage" -match "\((\d+)/(\d+)\)") {
                $coveredBranches = [int] $Matches[1]
                $totalBranches = [int] $Matches[2]

                if (-not $branchTotalsBySourceLine.ContainsKey($sourceLineKey)) {
                    $branchTotalsBySourceLine[$sourceLineKey] = 0
                    $branchCoveredBySourceLine[$sourceLineKey] = 0
                }

                $branchTotalsBySourceLine[$sourceLineKey] = [math]::Max($branchTotalsBySourceLine[$sourceLineKey], $totalBranches)
                $branchCoveredBySourceLine[$sourceLineKey] = [math]::Max($branchCoveredBySourceLine[$sourceLineKey], $coveredBranches)
            }
        }
    }
}

$lineValid = $lineHitsBySourceLine.Count
$lineCovered = @($lineHitsBySourceLine.GetEnumerator() | Where-Object { $_.Value -gt 0 }).Count
$branchValid = ($branchTotalsBySourceLine.Values | Measure-Object -Sum).Sum
$branchCovered = ($branchCoveredBySourceLine.Values | Measure-Object -Sum).Sum

$linePercent = if ($lineValid -eq 0) { [decimal] 100 } else { [math]::Round(($lineCovered / $lineValid) * 100, 2) }
$branchPercent = if ($branchValid -eq 0) { [decimal] 100 } else { [math]::Round(($branchCovered / $branchValid) * 100, 2) }

$coveragePercent = [math]::Min($linePercent, $branchPercent)

$color = if ($coveragePercent -ge 99) {
    "brightgreen"
}
elseif ($coveragePercent -ge 95) {
    "green"
}
elseif ($coveragePercent -ge 90) {
    "yellowgreen"
}
elseif ($coveragePercent -ge 80) {
    "yellow"
}
else {
    "red"
}

$badge = [ordered] @{
    schemaVersion = 1
    label = "Coverage"
    message = ("lines {0:N2}% | branches {1:N2}%" -f $linePercent, $branchPercent)
    color = $color
}

$directory = Split-Path -Parent $BadgePath
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

($badge | ConvertTo-Json -Compress) + [Environment]::NewLine | Set-Content -LiteralPath $BadgePath -Encoding utf8

Write-Host ("Line coverage:   {0}/{1} ({2:N2}%)" -f $lineCovered, $lineValid, $linePercent)
Write-Host ("Branch coverage: {0}/{1} ({2:N2}%)" -f $branchCovered, $branchValid, $branchPercent)
Write-Host "Badge written to $BadgePath"

if ($linePercent -lt $MinimumLineCoverage) {
    throw ("Line coverage {0:N2}% is below the required {1:N2}%." -f $linePercent, $MinimumLineCoverage)
}

if ($branchPercent -lt $MinimumBranchCoverage) {
    throw ("Branch coverage {0:N2}% is below the required {1:N2}%." -f $branchPercent, $MinimumBranchCoverage)
}

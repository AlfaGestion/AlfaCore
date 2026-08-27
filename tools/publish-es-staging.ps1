param(
    [string]$OutputPath = "artifacts/es-staging-publish"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/AlfaCore/AlfaCore.csproj"
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))

if (-not $resolvedOutput.StartsWith([System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts")), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "El publish ES debe quedar dentro de artifacts/."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

dotnet publish $projectPath -c Release --no-restore -o $resolvedOutput /p:UseAppHost=false
if ($LASTEXITCODE -ne 0) { throw "Falló dotnet publish." }

$commit = (git -C $repositoryRoot rev-parse HEAD).Trim()
$dirty = -not [string]::IsNullOrWhiteSpace((git -C $repositoryRoot status --porcelain))
$builtAtUtc = [DateTime]::UtcNow.ToString("O")
$moduleVersion = Select-String -LiteralPath (Join-Path $repositoryRoot "src/AlfaCore/wwwroot/js/whatsappEmbeddedSignup.js") -Pattern 'MODULE_VERSION\s*=\s*"([^"]+)"' |
    Select-Object -First 1 | ForEach-Object { $_.Matches[0].Groups[1].Value }

$version = @(
    "commit=$commit"
    "workingTreeDirty=$($dirty.ToString().ToLowerInvariant())"
    "builtAtUtc=$builtAtUtc"
    "configuration=Release"
    "targetFramework=net8.0"
    "embeddedSignupModuleVersion=$moduleVersion"
)
[System.IO.File]::WriteAllLines((Join-Path $resolvedOutput "build-version.txt"), [string[]]$version, [System.Text.UTF8Encoding]::new($false))

$inventoryPath = Join-Path $resolvedOutput "build-inventory.sha256"
$inventory = Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File |
    Where-Object { $_.FullName -ne $inventoryPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($resolvedOutput.TrimEnd('\').Length).TrimStart('\').Replace('\', '/')
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
[System.IO.File]::WriteAllLines($inventoryPath, [string[]]$inventory, [System.Text.UTF8Encoding]::new($false))

Write-Host "Publish preparado en $resolvedOutput"
Write-Host "Inventario: $inventoryPath"

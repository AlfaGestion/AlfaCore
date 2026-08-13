param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$sourceText = [System.IO.File]::ReadAllText($Source)
$oldLine = '        builder.Services.AddScoped<ICargaViajesService, CargaViajesService>();'
$newBlock = @'
        builder.Services.AddScoped<CargaViajesService>();
        builder.Services.AddScoped<ICargaViajesService>(sp =>
            CargaViajesServiceSaveTarifaProxy.Create(sp.GetRequiredService<CargaViajesService>()));
'@

if (-not $sourceText.Contains($oldLine)) {
    throw "No se encontró la línea de registro de ICargaViajesService en Program.cs."
}

$patchedText = $sourceText.Replace($oldLine, $newBlock)

$destinationDir = Split-Path -Path $Destination -Parent
if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
    [System.IO.Directory]::CreateDirectory($destinationDir) | Out-Null
}

[System.IO.File]::WriteAllText($Destination, $patchedText, [System.Text.UTF8Encoding]::new($false))

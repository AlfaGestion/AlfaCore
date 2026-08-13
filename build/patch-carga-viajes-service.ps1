param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$sourceText = [System.IO.File]::ReadAllText($Source)

$pattern1 = '(?s)            var adicionalFijo2PideCantidadColumn = FirstExistingColumnOrNull\(columns, "ADICIONALFIJO2PIDECANTIDAD", "ADICIONAL_FIJO2_PIDE_CANTIDAD"\);\s*            var adicionalFijo3DescripcionColumn = FirstExistingColumnOrNull\(columns, "ADICIONALFIJO3DESCRIPCION", "ADICIONAL_FIJO3_DESCRIPCION"\);\s*            var adicionalFijo3ImporteColumn = FirstExistingColumnOrNull\(columns, "ADICIONALFIJO3IMPORTE", "ADICIONAL_FIJO3_IMPORTE"\);\s*            var adicionalFijo3PideCantidadColumn = FirstExistingColumnOrNull\(columns, "ADICIONALFIJO3PIDECANTIDAD", "ADICIONAL_FIJO3_PIDE_CANTIDAD"\);\s*\s*            var idLista = request.IdLista.Trim\(\).ToUpperInvariant\(\);'
$replacement1 = @'
            var adicionalFijo2PideCantidadColumn = FirstExistingColumnOrNull(columns, "ADICIONALFIJO2PIDECANTIDAD", "ADICIONAL_FIJO2_PIDE_CANTIDAD");
            var adicionalFijo3DescripcionColumn = FirstExistingColumnOrNull(columns, "ADICIONALFIJO3DESCRIPCION", "ADICIONAL_FIJO3_DESCRIPCION");
            var adicionalFijo3ImporteColumn = FirstExistingColumnOrNull(columns, "ADICIONALFIJO3IMPORTE", "ADICIONAL_FIJO3_IMPORTE");
            var adicionalFijo3PideCantidadColumn = FirstExistingColumnOrNull(columns, "ADICIONALFIJO3PIDECANTIDAD", "ADICIONAL_FIJO3_PIDE_CANTIDAD");

            if (request.Id <= 0)
                request.IdLista = await EnsureUniqueTarifaListaAsync(cn, idListaColumn, request.IdLista, token);

            var idLista = request.IdLista.Trim().ToUpperInvariant();
'@

$patchedText = [regex]::Replace($sourceText, $pattern1, $replacement1, 1)

$helpersBlock = @'
    private static async Task<string> EnsureUniqueTarifaListaAsync(SqlConnection cn, string idListaColumn, string currentIdLista, CancellationToken ct)
    {
        var normalized = (currentIdLista ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(idListaColumn))
            return normalized;

        if (!string.IsNullOrWhiteSpace(normalized) && !await ExistsByCodeAsync(cn, "TA_TARIFA", idListaColumn, normalized, ct))
            return normalized;

        return await GenerateUniqueTarifaListaAsync(cn, idListaColumn, ct);
    }

    private static async Task<string> GenerateUniqueTarifaListaAsync(SqlConnection cn, string idListaColumn, CancellationToken ct)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const int maxAttempts = 512;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = BuildRandomTarifaListaCodigo(alphabet);
            if (!await ExistsByCodeAsync(cn, "TA_TARIFA", idListaColumn, candidate, ct))
                return candidate;
        }

        for (var suffix = 1; suffix <= 9999; suffix++)
        {
            var candidate = suffix.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            if (!await ExistsByCodeAsync(cn, "TA_TARIFA", idListaColumn, candidate, ct))
                return candidate;
        }

        throw new InvalidOperationException("No se pudo generar un codigo de lista unico para la nueva tarifa.");
    }

    private static string BuildRandomTarifaListaCodigo(string alphabet)
    {
        var chars = new char[4];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];

        return new string(chars);
    }
'@

$pattern2 = '(?s)    private static async Task<bool> ExistsByCodeAsync\(SqlConnection cn, string table, string column, string code, CancellationToken ct\)\s*\{\s*.*?return count > 0;\s*    \}'
$patchedText = [regex]::Replace($patchedText, $pattern2, {
    param($match)
    $match.Value + "`r`n`r`n" + $helpersBlock
}, 1)

$destinationDir = Split-Path -Path $Destination -Parent
if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
    [System.IO.Directory]::CreateDirectory($destinationDir) | Out-Null
}

[System.IO.File]::WriteAllText($Destination, $patchedText, [System.Text.UTF8Encoding]::new($false))

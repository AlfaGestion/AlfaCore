param(
    [string]$Carpeta = 'Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT',
    [Parameter(Mandatory = $true)][string]$Salida
)

# Ejecutar con Windows PowerShell de 32 bits. Solo abre metadatos: no conecta,
# refresca, imprime, exporta datos guardados ni modifica los RPT originales.
$ErrorActionPreference = 'Stop'
$nombres = @('RP_CtaCte2', 'RP_CtaCte2UNeg', 'RP_CtaCteResumidoNW',
    'RP_CtaCteResumidoNW_UNEG', 'RP_AnaliticoCtaCteNW', 'RP_CtaCteSaldos02',
    'RP_CtaCteSaldos02a', 'RP_CtaCteSaldos02b', 'RP_CtaCteSaldos01',
    'RP_VentasCompras2', 'RP_CobPag', 'RP_CobPag_Detallado', 'RP_CtaCteProyeccion')
$app = New-Object -ComObject CrystalRuntime.Application
try {
    $resultado = foreach ($nombre in $nombres) {
        $ruta = Join-Path $Carpeta ($nombre + '.rpt')
        $reporte = $null
        try {
            $reporte = $app.OpenReport($ruta, 1)
            $tablas = @(foreach ($tabla in $reporte.Database.Tables) {
                @{ Nombre = $tabla.Name; Ubicacion = $tabla.Location;
                   Campos = @(foreach ($campo in $tabla.Fields) { $campo.Name }) }
            })
            $formulas = @(foreach ($formula in $reporte.FormulaFields) {
                @{ Nombre = $formula.Name; Texto = $formula.Text }
            })
            $grupos = @(foreach ($grupo in $reporte.Areas) {
                foreach ($seccion in $grupo.Sections) {
                    foreach ($objeto in $seccion.ReportObjects) {
                        if ($objeto.Kind -eq 5) { $objeto.SubreportName }
                    }
                }
            })
            $agrupaciones = @(foreach ($area in $reporte.Areas) {
                if ($area.Kind -eq 3) { @{ Campo = $area.GroupConditionField.Name } }
            })
            $orden = @(foreach ($campoOrden in $reporte.RecordSortFields) {
                @{ Campo = $campoOrden.Field.Name; Direccion = $campoOrden.SortDirection }
            })
            @{ Archivo = $nombre + '.rpt'; SHA256 = (Get-FileHash -LiteralPath $ruta -Algorithm SHA256).Hash;
               Tablas = $tablas; Formulas = $formulas; Subreportes = $grupos;
               Agrupaciones = $agrupaciones; Orden = $orden;
               Seleccion = $reporte.RecordSelectionFormula }
        }
        catch { @{ Archivo = $nombre + '.rpt'; Error = $_.Exception.Message } }
        finally {
            if ($null -ne $reporte) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($reporte) }
        }
    }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Salida),
        (ConvertTo-Json -InputObject @($resultado) -Depth 12), (New-Object Text.UTF8Encoding($false)))
}
finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($app) }

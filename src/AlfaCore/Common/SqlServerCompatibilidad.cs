namespace AlfaCore.Common;

internal static class SqlServerCompatibilidad
{
    // Expresiones y tipos provienen de constantes del servicio, nunca de entrada del usuario.
    internal static string EnteroONull(string expresion, bool bigint = false)
    {
        var valor = $"LTRIM(RTRIM({expresion}))";
        var digitos = $"(CASE WHEN LEFT({valor},1) IN ('+','-') THEN SUBSTRING({valor},2,8000) ELSE {valor} END)";
        var magnitud = $"COALESCE(NULLIF(SUBSTRING({digitos},PATINDEX('%[^0]%',{digitos}+'1'),LEN({digitos})),''),'0')";
        var limite = bigint
            ? $"CASE WHEN LEFT({valor},1)='-' THEN '9223372036854775808' ELSE '9223372036854775807' END"
            : $"CASE WHEN LEFT({valor},1)='-' THEN '2147483648' ELSE '2147483647' END";
        var ancho = bigint ? 19 : 10;
        var tipo = bigint ? "bigint" : "int";
        return $"""
            CASE WHEN {digitos}<>'' AND {digitos} COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9]%'
                AND (LEN({magnitud})<{ancho} OR (LEN({magnitud})={ancho} AND {magnitud} COLLATE Latin1_General_BIN2<=({limite})))
            THEN CONVERT({tipo},(CASE WHEN LEFT({valor},1)='-' THEN '-' ELSE '' END)+{magnitud}) END
            """;
    }

    internal static string Paginar(string serverVersion, string fila, string offset = "@Skip", bool ordenar = true)
        => Version.Parse(serverVersion).Major < 11
            ? $"WHERE {fila}>{offset} AND {fila}<=CONVERT(bigint,{offset})+@PageSize" + (ordenar ? $" ORDER BY {fila}" : "")
            : $"ORDER BY {fila} OFFSET {offset} ROWS FETCH NEXT @PageSize ROWS ONLY";
}

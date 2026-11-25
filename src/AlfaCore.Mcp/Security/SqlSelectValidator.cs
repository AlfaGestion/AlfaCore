using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AlfaCore.Mcp.Security;

public sealed record ValidacionSql(bool Valido, string? Error);

/// <summary>
/// Validador de consultar_sql: UNA sentencia SELECT, solo sobre objetos del esquema <c>dir</c>, sin variables, sin
/// SELECT INTO, sin funciones de tabla / OPENROWSET / servidores vinculados.
/// Es una capa SECUNDARIA: la barrera real son los permisos del login (SELECT solo sobre el esquema dir).
/// </summary>
public static class SqlSelectValidator
{
    public const string EsquemaPermitido = "dir";
    public const int LargoMaximo = 8000;

    public static ValidacionSql Validar(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Falla("La consulta está vacía.");
        if (sql.Length > LargoMaximo) return Falla($"La consulta supera {LargoMaximo} caracteres.");

        var parser = new TSql100Parser(initialQuotedIdentifiers: true);
        using var lector = new StringReader(sql);
        var fragmento = parser.Parse(lector, out var errores);
        if (errores.Count > 0)
            return Falla($"SQL inválido (línea {errores[0].Line}): {errores[0].Message}");

        if (fragmento is not TSqlScript script || script.Batches.Count != 1 || script.Batches[0].Statements.Count != 1)
            return Falla("Se permite una única sentencia SELECT.");

        if (script.Batches[0].Statements[0] is not SelectStatement select)
            return Falla("Solo se permiten consultas SELECT.");

        var visitor = new Visitante(select);
        select.Accept(visitor);
        return visitor.Error is null ? new ValidacionSql(true, null) : Falla(visitor.Error);
    }

    private static ValidacionSql Falla(string error) => new(false, error);

    private sealed class Visitante : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _ctes = new(StringComparer.OrdinalIgnoreCase);
        public string? Error { get; private set; }

        public Visitante(SelectStatement select)
        {
            var ctes = select.WithCtesAndXmlNamespaces?.CommonTableExpressions;
            if (ctes is not null)
                foreach (var cte in ctes) _ctes.Add(cte.ExpressionName.Value);
        }

        private void Rechazar(string mensaje) => Error ??= mensaje;

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null) Rechazar("SELECT INTO no está permitido.");
        }

        public override void Visit(SelectSetVariable node) => Rechazar("No se permiten variables.");
        public override void Visit(VariableReference node) => Rechazar("No se permiten variables ni parámetros.");
        public override void Visit(GlobalVariableExpression node) => Rechazar("No se permiten variables globales (@@).");

        public override void Visit(NamedTableReference node)
        {
            var obj = node.SchemaObject;
            if (obj.SchemaIdentifier is null && obj.DatabaseIdentifier is null && obj.ServerIdentifier is null
                && _ctes.Contains(obj.BaseIdentifier.Value))
                return; // referencia a una CTE definida en la propia consulta

            if (obj.ServerIdentifier is not null || obj.DatabaseIdentifier is not null)
                Rechazar("No se permiten nombres de servidor ni de base: usar dir.<vista>.");
            else if (obj.SchemaIdentifier is null || !obj.SchemaIdentifier.Value.Equals(EsquemaPermitido, StringComparison.OrdinalIgnoreCase))
                Rechazar($"Solo se pueden consultar objetos del esquema {EsquemaPermitido} (ej. dir.vw_Ventas). Objeto rechazado: {obj.BaseIdentifier.Value}.");
        }

        public override void Visit(TableReference node)
        {
            // Lista blanca de formas de origen de datos; todo lo demas (funciones de tabla, OPENROWSET, OPENQUERY,
            // OPENJSON, variables de tabla, etc.) se rechaza.
            switch (node)
            {
                case NamedTableReference:
                case QueryDerivedTable:
                case QualifiedJoin:
                case UnqualifiedJoin:
                case JoinParenthesisTableReference:
                case PivotedTableReference:
                case UnpivotedTableReference:
                    break;
                default:
                    Rechazar($"Origen de datos no permitido ({node.GetType().Name}). Solo vistas del esquema {EsquemaPermitido}.");
                    break;
            }
        }

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is not null)
                Rechazar("No se permiten funciones con esquema o servidor (solo funciones propias de SQL Server).");
        }
    }
}

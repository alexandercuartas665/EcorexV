using System.Text.RegularExpressions;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Prepara el CommandText y la lista PLANA de parametros ADO a partir de los parametros ENLAZADOS,
/// expandiendo los MULTI-VALOR (SSRS `... IN (@p)`). Pieza PURA (sin BD): el ejecutor la usa para armar el
/// comando; asi la logica de expansion es testeable sin driver.
///
/// Para un parametro multi-valor con N valores, el token <c>@p</c> del CommandText se reemplaza por
/// <c>@p__0, @p__1, ..., @p__{N-1}</c> (respetando el <c>(...)</c> del IN existente) y se emite un
/// <see cref="ExternalFlatParameter"/> TIPADO por valor: cero interpolacion de texto, misma proteccion
/// anti-inyeccion que un escalar. Con 0 valores se sustituye por <c>NULL</c> (=> `IN (NULL)`, 0 filas, sin
/// error de sintaxis). El reemplazo del token usa limite de palabra: <c>@p</c> no pisa <c>@p2</c> ni
/// <c>@precio</c>.
/// </summary>
public static class ExternalCommandBuilder
{
    public static (string Sql, IReadOnlyList<ExternalFlatParameter> Parameters) ExpandInLists(
        string commandText, IReadOnlyList<ExternalBoundParameter> bound)
    {
        var sql = commandText ?? string.Empty;
        var flat = new List<ExternalFlatParameter>();

        foreach (var p in bound)
        {
            var name = p.Name.StartsWith('@') ? p.Name[1..] : p.Name;

            if (p.Values is null)
            {
                // Escalar: el token @name se deja como esta y se enlaza un unico parametro.
                flat.Add(new ExternalFlatParameter("@" + name, p.Value));
                continue;
            }

            if (p.Values.Count == 0)
            {
                // Multi-valor sin valores: IN (NULL) -> ninguna fila, sin romper la sintaxis.
                sql = ReplaceToken(sql, name, "NULL");
                continue;
            }

            var placeholders = new List<string>(p.Values.Count);
            for (var i = 0; i < p.Values.Count; i++)
            {
                var ph = $"@{name}__{i}";
                placeholders.Add(ph);
                flat.Add(new ExternalFlatParameter(ph, p.Values[i]));
            }
            sql = ReplaceToken(sql, name, string.Join(", ", placeholders));
        }

        return (sql, flat);
    }

    // Reemplaza el token @name SOLO como palabra completa: no toca @name2, @name_x ni @nameXYZ.
    private static string ReplaceToken(string sql, string name, string replacement)
        => Regex.Replace(sql, "@" + Regex.Escape(name) + "(?![A-Za-z0-9_])", replacement);
}

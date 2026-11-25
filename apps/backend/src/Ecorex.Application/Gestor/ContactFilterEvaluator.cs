using System.Globalization;
using Ecorex.Application.Directorio;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Evaluador COMPARTIDO de los criterios de un <c>TerceroFiltro</c> sobre un contacto proyectado
/// (000740). Extraido de <see cref="GestorContactosService"/> para que el "Filtrar ahora" del Gestor
/// y el MOTOR de acciones por filtro (ADR-0056, Fase 2) resuelvan el MISMO segmento con la MISMA logica
/// (una sola fuente de verdad). Los campos de FICHA (dinamicos de FichasJson) se escriben "f:{FieldKey}" y se
/// comparan contra <see cref="Row.Fichas"/>: una fila sin ese dato (p.ej. un prospecto, que no tiene fichas) lo
/// trata como vacio. Otros campos desconocidos se IGNORAN (no excluyen filas).
/// </summary>
public static class ContactFilterEvaluator
{
    /// <summary>Proyeccion minima de un tercero necesaria para evaluar los criterios.</summary>
    public sealed record Row(
        string Nombre,
        string? Ciudad,
        string? Vendedor,
        string? Sector,
        string? Cargo,
        TerceroPerfil Perfiles,
        TerceroEstado Estado,
        IReadOnlyDictionary<string, string>? Fichas = null);

    /// <summary>Evalua los criterios encadenados por su conector (Y = AND, O = OR), de izquierda a
    /// derecha SIN precedencia (el primer conector se ignora). Lista vacia = todos pasan. Ej.:
    /// "ciudad=bogota Y sector=salud O cargo=gerente" = ((ciudad Y sector) O cargo).</summary>
    public static bool MatchesAll(Row row, IReadOnlyList<FiltroCriterio> criterios)
    {
        if (criterios.Count == 0) { return true; }
        var result = Matches(row, criterios[0]);
        for (var i = 1; i < criterios.Count; i++)
        {
            var m = Matches(row, criterios[i]);
            result = string.Equals((criterios[i].Conector ?? "Y").Trim(), "O", System.StringComparison.OrdinalIgnoreCase)
                ? result || m
                : result && m;
        }
        return result;
    }

    public static bool Matches(Row row, FiltroCriterio criterio)
    {
        var campo = (criterio.Campo ?? string.Empty).Trim().ToLowerInvariant();
        var op = (criterio.Operador ?? "=").Trim();
        var valor = criterio.Valor ?? string.Empty;

        // Campo de FICHA ("f:{FieldKey}"): el valor guardado en las fichas del tercero (vacio si no lo tiene).
        if (TerceroFichaValues.FieldKeyOf(criterio.Campo) is string fieldKey)
        {
            return Compare(FichaValue(row.Fichas, fieldKey), op, valor);
        }

        // Perfil: se compara contra el flag [Flags] TerceroPerfil.
        if (campo == "perfil")
        {
            if (!Enum.TryParse<TerceroPerfil>(valor, ignoreCase: true, out var perfil) || perfil == TerceroPerfil.Ninguno)
            {
                return true; // valor de perfil desconocido: no filtra.
            }
            var tiene = (row.Perfiles & perfil) == perfil;
            return op switch
            {
                "!=" => !tiene,
                _ => tiene // "=", "LIKE" y demas: posee el perfil.
            };
        }

        var actual = campo switch
        {
            "ciudad" => row.Ciudad,
            "vendedor" => row.Vendedor,
            "nombre" => row.Nombre,
            "sector" => row.Sector,
            "cargo" => row.Cargo,
            "estado" => row.Estado.ToString(),
            _ => null
        };
        // Campo no soportado: se ignora el criterio.
        if (campo is not ("ciudad" or "vendedor" or "nombre" or "sector" or "cargo" or "estado"))
        {
            return true;
        }

        return Compare(actual, op, valor);
    }

    private static string? FichaValue(IReadOnlyDictionary<string, string>? fichas, string fieldKey)
    {
        if (fichas is null) { return null; }
        if (fichas.TryGetValue(fieldKey, out var v)) { return v; }
        // El diccionario puede venir con comparador ordinal (Filtrables del listado): busqueda sin mayusculas.
        foreach (var (k, value) in fichas)
        {
            if (string.Equals(k, fieldKey, StringComparison.OrdinalIgnoreCase)) { return value; }
        }
        return null;
    }

    private static bool Compare(string? actual, string op, string valor)
    {
        switch (op)
        {
            case "=":
                return string.Equals(actual ?? string.Empty, valor, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return !string.Equals(actual ?? string.Empty, valor, StringComparison.OrdinalIgnoreCase);
            case "LIKE":
                return actual is not null && actual.Contains(valor, StringComparison.OrdinalIgnoreCase);
            case ">":
            case "<":
                if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    return op == ">" ? a > b : a < b;
                }
                return false;
            default:
                return false;
        }
    }
}

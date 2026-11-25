using System.Globalization;
using Ecorex.Application.Directorio;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Evaluador COMPARTIDO de los criterios de un <c>TerceroFiltro</c> sobre un contacto proyectado
/// (000740). Extraido de <see cref="GestorContactosService"/> para que el "Filtrar ahora" del Gestor
/// y el MOTOR de acciones por filtro (ADR-0056, Fase 2) resuelvan el MISMO segmento con la MISMA logica
/// (una sola fuente de verdad).
/// <para>Campos FIJOS (<see cref="CamposFijos"/>): los mismos del Directorio. Campos de FICHA (dinamicos de
/// FichasJson): "f:{FieldKey}", comparados contra <see cref="Row.Fichas"/>; una fila sin ese dato (p.ej. un
/// prospecto, que no tiene fichas) lo trata como vacio. Campos desconocidos se IGNORAN (no excluyen).</para>
/// <para>Operadores (<see cref="Operadores"/>): = | != | LIKE (contiene) | NOT LIKE | IN (es uno de, separados
/// por coma) | NOT IN | EMPTY | NOT EMPTY | &gt; | &lt; (numero o fecha). Sin distinguir mayusculas.</para>
/// </summary>
public static class ContactFilterEvaluator
{
    /// <summary>Campos fijos soportados (clave del criterio). "correo" acepta tambien "email".</summary>
    public static readonly IReadOnlyList<string> CamposFijos = new[]
    {
        "nombre", "naturaleza", "perfil", "identificacion", "vendedor", "estado", "ciudad", "sector", "cargo",
        "correo", "telefono", "empresa",
    };

    /// <summary>Operadores soportados (valor guardado en el criterio).</summary>
    public static readonly IReadOnlyList<string> Operadores = new[]
    {
        "=", "!=", "LIKE", "NOT LIKE", "IN", "NOT IN", "EMPTY", "NOT EMPTY", ">", "<",
    };

    /// <summary>True si el operador no usa valor (vacio / tiene contenido).</summary>
    public static bool SinValor(string? op) => NormalizeOp(op) is "EMPTY" or "NOT EMPTY";

    /// <summary>Proyeccion de un tercero (o prospecto) necesaria para evaluar los criterios.</summary>
    public sealed record Row(
        string Nombre,
        string? Ciudad,
        string? Vendedor,
        string? Sector,
        string? Cargo,
        TerceroPerfil Perfiles,
        TerceroEstado Estado,
        IReadOnlyDictionary<string, string>? Fichas = null,
        string? Identificacion = null,
        string? Naturaleza = null,
        string? Correo = null,
        string? Telefono = null,
        string? Empresa = null);

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
            result = string.Equals((criterios[i].Conector ?? "Y").Trim(), "O", StringComparison.OrdinalIgnoreCase)
                ? result || m
                : result && m;
        }
        return result;
    }

    public static bool Matches(Row row, FiltroCriterio criterio)
    {
        var campo = (criterio.Campo ?? string.Empty).Trim().ToLowerInvariant();
        var op = NormalizeOp(criterio.Operador);
        var valor = criterio.Valor ?? string.Empty;

        // Campo de FICHA ("f:{FieldKey}"): el valor guardado en las fichas del tercero (vacio si no lo tiene).
        if (TerceroFichaValues.FieldKeyOf(criterio.Campo) is string fieldKey)
        {
            return Compare(FichaValue(row.Fichas, fieldKey), op, valor);
        }

        // Perfil: flags [Flags] TerceroPerfil (un tercero puede tener varios).
        if (campo == "perfil")
        {
            return MatchPerfil(row.Perfiles, op, valor);
        }

        if (!TryGetFijo(row, campo, out var actual))
        {
            return true; // campo desconocido: se ignora el criterio.
        }
        return Compare(actual, op, valor);
    }

    private static bool TryGetFijo(Row row, string campo, out string? actual)
    {
        actual = campo switch
        {
            "nombre" => row.Nombre,
            "ciudad" => row.Ciudad,
            "vendedor" => row.Vendedor,
            "sector" => row.Sector,
            "cargo" => row.Cargo,
            "estado" => row.Estado.ToString(),
            "identificacion" => row.Identificacion,
            "naturaleza" => row.Naturaleza,
            "correo" or "email" => row.Correo,
            "telefono" => row.Telefono,
            "empresa" => row.Empresa,
            _ => null
        };
        return campo is "nombre" or "ciudad" or "vendedor" or "sector" or "cargo" or "estado" or "identificacion"
            or "naturaleza" or "correo" or "email" or "telefono" or "empresa";
    }

    /// <summary>Operador canonico: mayusculas, espacios simples; alias "contains"/"ncontains"/"eq"/"neq".</summary>
    public static string NormalizeOp(string? op)
    {
        var o = string.Join(' ', (op ?? "=").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return o switch
        {
            "" or "EQ" or "==" => "=",
            "NEQ" or "<>" => "!=",
            "CONTAINS" => "LIKE",
            "NCONTAINS" or "NOTLIKE" => "NOT LIKE",
            "NOTIN" => "NOT IN",
            "NEMPTY" or "NOTEMPTY" => "NOT EMPTY",
            _ => o
        };
    }

    /// <summary>Identificacion a comparar: el numero/valor SIN prefijo de tipo. Acepta el texto formateado del
    /// listado ("NIT 900123", "CC 123", "Tel 300...", "Sin identificacion" -> null).</summary>
    public static string? IdentificacionDe(string? texto)
    {
        var t = (texto ?? string.Empty).Trim();
        if (t.Length == 0 || string.Equals(t, "Sin identificacion", StringComparison.OrdinalIgnoreCase)) { return null; }
        foreach (var prefijo in new[] { "NIT ", "CC ", "Tel " })
        {
            if (t.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) { return t[prefijo.Length..].Trim(); }
        }
        return t;
    }

    /// <summary>Naturaleza legible del tercero.</summary>
    public static string NaturalezaDe(bool esEmpresa) => esEmpresa ? "Empresa" : "Persona";

    /// <summary>Valores de un IN: separados por coma (o salto de linea), recortados, sin vacios.</summary>
    public static IReadOnlyList<string> SplitList(string? valor)
        => (valor ?? string.Empty).Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchPerfil(TerceroPerfil perfiles, string op, string valor)
    {
        switch (op)
        {
            case "EMPTY": return perfiles == TerceroPerfil.Ninguno;
            case "NOT EMPTY": return perfiles != TerceroPerfil.Ninguno;
        }

        var pedidos = SplitList(valor)
            .Select(v => Enum.TryParse<TerceroPerfil>(v, ignoreCase: true, out var p) ? p : TerceroPerfil.Ninguno)
            .Where(p => p != TerceroPerfil.Ninguno)
            .ToList();
        if (pedidos.Count == 0) { return true; } // valor de perfil desconocido: no filtra.

        var alguno = pedidos.Any(p => (perfiles & p) == p);
        return op switch
        {
            "!=" or "NOT LIKE" or "NOT IN" => !alguno,
            _ => alguno // "=", "LIKE", "IN": posee el perfil (alguno de ellos).
        };
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
        var a = (actual ?? string.Empty).Trim();
        var v = valor.Trim();
        switch (op)
        {
            case "=":
                return string.Equals(a, v, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return !string.Equals(a, v, StringComparison.OrdinalIgnoreCase);
            case "LIKE":
                return a.Length > 0 && a.Contains(v, StringComparison.OrdinalIgnoreCase);
            case "NOT LIKE":
                return !(a.Length > 0 && a.Contains(v, StringComparison.OrdinalIgnoreCase));
            case "IN":
                return SplitList(valor).Any(x => string.Equals(a, x, StringComparison.OrdinalIgnoreCase));
            case "NOT IN":
                return !SplitList(valor).Any(x => string.Equals(a, x, StringComparison.OrdinalIgnoreCase));
            case "EMPTY":
                return a.Length == 0;
            case "NOT EMPTY":
                return a.Length > 0;
            case ">":
            case "<":
                if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var na)
                    && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
                {
                    return op == ">" ? na > nb : na < nb;
                }
                if (DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var da)
                    && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var db))
                {
                    return op == ">" ? da > db : da < db;
                }
                return false;
            default:
                return false;
        }
    }
}

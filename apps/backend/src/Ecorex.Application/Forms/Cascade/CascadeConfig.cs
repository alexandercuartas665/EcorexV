using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Forms.Calc;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Cascade;

/// <summary>
/// Esquema (contrato) del control <see cref="FormControlType.CascadeConfigurator"/>: un motor GENERICO
/// que reproduce un configurador en cascada + tablas dinamicas leyendo SU taxonomia desde
/// <see cref="Ecorex.Domain.Entities.FormQuestion.CascadeConfigJson"/>. Nada hardcodeado: el mismo motor
/// sirve cualquier taxonomia de cualquier tenant, y el diseniador de formularios puede componer y editar
/// esta config sin tocar codigo.
///
/// Principios del contrato:
/// - Las opciones de cada nivel pueden ser INLINE (<see cref="CascadeLevel.Options"/>) o venir de una
///   FUENTE (<see cref="CascadeLevel.Source"/>) reusando <see cref="FormSourceKind"/> + el lookup del
///   sistema (DataContainer / Tercero / Item). Nunca inline-only.
/// - Las columnas de las tablas reusan el pipeline de calculo de GridDetail: la formula por fila
///   (<see cref="CascadeColumn.Calc"/>) y el roll-up (<see cref="CascadeColumn.Rollup"/>) se validan y
///   evaluan con <see cref="FormExpressionEvaluator"/> / <see cref="FormGridCalculator"/>. No hay un
///   motor de calculo propio.
/// - El registro capturado se persiste en <c>FormResponse.Data</c> (jsonb) reportable como cualquier
///   otro formulario (ver <see cref="CascadeResultShape"/>).
/// </summary>
public sealed record CascadeConfig(
    int Version,
    IReadOnlyList<CascadeLevel> Levels,
    IReadOnlyDictionary<string, CascadeColumn> Columns,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnSets,
    CascadeTable Table)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Deserializa el JSON. Devuelve null y <paramref name="error"/> si es invalido.</summary>
    public static CascadeConfig? Parse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "La configuracion del cascade esta vacia.";
            return null;
        }

        CascadeConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<CascadeConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = "JSON invalido: " + ex.Message;
            return null;
        }

        if (cfg is null)
        {
            error = "La configuracion del cascade esta vacia.";
            return null;
        }

        error = Validate(cfg);
        return error is null ? cfg : null;
    }

    /// <summary>Validacion estructural del contrato. Devuelve el primer error o null si es valido.</summary>
    public static string? Validate(CascadeConfig cfg)
    {
        if (cfg.Levels is null || cfg.Levels.Count == 0)
        {
            return "Debe haber al menos un nivel.";
        }

        var levelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSoFar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allOptionIds = new HashSet<string>(StringComparer.Ordinal);
        var columns = cfg.Columns ?? new Dictionary<string, CascadeColumn>();
        var columnSets = cfg.ColumnSets ?? new Dictionary<string, IReadOnlyList<string>>();

        foreach (var lvl in cfg.Levels)
        {
            if (string.IsNullOrWhiteSpace(lvl.Key))
            {
                return "Cada nivel necesita 'key'.";
            }
            if (!levelKeys.Add(lvl.Key))
            {
                return $"Nivel duplicado: '{lvl.Key}'.";
            }
            if (lvl.Select is not ("single" or "multi"))
            {
                return $"Nivel '{lvl.Key}': 'select' debe ser 'single' o 'multi'.";
            }
            // La dependencia debe apuntar a un nivel ANTERIOR (cascada hacia adelante, sin ciclos).
            if (!string.IsNullOrWhiteSpace(lvl.DependsOn) && !seenSoFar.Contains(lvl.DependsOn))
            {
                return $"Nivel '{lvl.Key}': 'dependsOn' = '{lvl.DependsOn}' no es un nivel anterior.";
            }
            // Origen de opciones: inline XOR source (nunca ambos, nunca ninguno).
            var hasInline = lvl.Options is { Count: > 0 };
            var hasSource = lvl.Source is not null;
            if (hasInline == hasSource)
            {
                return $"Nivel '{lvl.Key}': define 'options' (inline) O 'source' (externa), no ambos ni ninguno.";
            }
            if (hasSource && lvl.Source!.Kind == FormSourceKind.Options)
            {
                return $"Nivel '{lvl.Key}': 'source.kind' no puede ser 'Options' (eso es inline).";
            }
            if (hasSource && string.IsNullOrWhiteSpace(lvl.Source!.Ref))
            {
                return $"Nivel '{lvl.Key}': 'source' necesita 'ref'.";
            }
            if (!string.IsNullOrWhiteSpace(lvl.FeedsColumn) && !columns.ContainsKey(lvl.FeedsColumn))
            {
                return $"Nivel '{lvl.Key}': 'feedsColumn' = '{lvl.FeedsColumn}' no existe en 'columns'.";
            }
            if (lvl.ColumnSetFrom is not (null or "self" or "parent"))
            {
                return $"Nivel '{lvl.Key}': 'columnSetFrom' debe ser 'self' o 'parent'.";
            }
            // Opciones inline: la columna de un juego referenciado debe existir.
            if (hasInline)
            {
                foreach (var op in lvl.Options!)
                {
                    if (string.IsNullOrWhiteSpace(op.Id))
                    {
                        return $"Nivel '{lvl.Key}': una opcion no tiene 'id'.";
                    }
                    if (!allOptionIds.Add(op.Id))
                    {
                        return $"Opcion con id duplicado (debe ser unico global): '{op.Id}'.";
                    }
                    if (!string.IsNullOrWhiteSpace(op.ColumnSet) && !columnSets.ContainsKey(op.ColumnSet))
                    {
                        return $"Nivel '{lvl.Key}', opcion '{op.Id}': columnSet '{op.ColumnSet}' no existe.";
                    }
                }
            }
            seenSoFar.Add(lvl.Key);
        }

        if (!cfg.Levels.Any(l => l.OpensTable))
        {
            return "Al menos un nivel debe tener 'opensTable': true.";
        }

        // Juegos de columnas: cada columna referenciada debe existir; la formula/roll-up se valida con
        // el MISMO evaluador del resto del sistema (no un motor propio).
        foreach (var (setName, cols) in columnSets)
        {
            if (cols is null || cols.Count == 0)
            {
                return $"El juego de columnas '{setName}' esta vacio.";
            }
            foreach (var colKey in cols)
            {
                if (!columns.ContainsKey(colKey))
                {
                    return $"Juego '{setName}': la columna '{colKey}' no esta definida en 'columns'.";
                }
            }
        }

        // Indice global de opciones inline (para resolver herencia de columnSet y validar 'parent').
        var optionIndex = BuildOptionIndex(cfg);

        // Integridad de 'parent': si una opcion declara padre, ese padre debe existir.
        foreach (var lvl in cfg.Levels)
        {
            if (lvl.Options is null)
            {
                continue;
            }
            foreach (var op in lvl.Options)
            {
                if (!string.IsNullOrWhiteSpace(op.Parent) && !optionIndex.ContainsKey(op.Parent))
                {
                    return $"Nivel '{lvl.Key}', opcion '{op.Id}': 'parent' = '{op.Parent}' no existe.";
                }
            }
        }

        // B1 - Resolucion MIXTA del columnSet: por opcion, en orden option.columnSet -> columnSet del
        // PADRE (recursivo) -> DefaultColumnSet del nivel. TODA opcion de un nivel que abre tabla debe
        // resolver a un columnSet existente. Solo se valida en niveles inline (los 'source' se resuelven
        // en runtime, cuando se conocen sus opciones).
        foreach (var lvl in cfg.Levels)
        {
            if (!lvl.OpensTable || lvl.Options is null)
            {
                continue;
            }
            foreach (var op in lvl.Options)
            {
                var resolved = ResolveColumnSet(op, lvl, optionIndex);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    return $"Nivel '{lvl.Key}', opcion '{op.Id}': no define ni hereda un columnSet.";
                }
                if (!columnSets.ContainsKey(resolved))
                {
                    return $"Nivel '{lvl.Key}', opcion '{op.Id}': el columnSet resuelto '{resolved}' no existe.";
                }
            }
        }

        foreach (var (colKey, col) in columns)
        {
            if (!string.IsNullOrWhiteSpace(col.Calc))
            {
                var calcError = FormExpressionEvaluator.Validate(col.Calc);
                if (calcError is not null)
                {
                    return $"Columna '{colKey}', formula: {calcError}";
                }
            }
            // 'rollup' NO es una formula: es el NOMBRE del destino de respuesta que recibe el agregado
            // (misma semantica que FormGridCalculator: rollups[col.Rollup] = agregado). No se evalua.
            if (!string.IsNullOrWhiteSpace(col.Agg)
                && !Enum.TryParse<FormAggregate>(col.Agg, ignoreCase: true, out _))
            {
                return $"Columna '{colKey}': 'agg' = '{col.Agg}' no es un agregado valido.";
            }
        }

        if (cfg.Table is null || cfg.Table.KeyBy is null || cfg.Table.KeyBy.Count == 0)
        {
            return "'table.keyBy' debe declarar al menos una clave (ej. ['portafolio','columnSet']).";
        }
        foreach (var key in cfg.Table.KeyBy)
        {
            if (!levelKeys.Contains(key) && !string.Equals(key, "columnSet", StringComparison.OrdinalIgnoreCase))
            {
                return $"'table.keyBy': '{key}' no es un nivel ni 'columnSet'.";
            }
        }

        // B2 - Obligatorias por fila iniciada: la lista puede mezclar columnas de distintos juegos (la
        // 1a columna cambia por juego). Aqui solo se exige que cada entrada sea una columna EXISTENTE o
        // el token '@first' (= 1a columna del juego de la fila). El RUNTIME aplica solo las que esten en
        // el columnSet de esa fila (ignora las ausentes) y respeta la semantica de "fila iniciada".
        if (cfg.Table.RequiredOnStartedRow is not null)
        {
            foreach (var req in cfg.Table.RequiredOnStartedRow)
            {
                if (!string.Equals(req, "@first", StringComparison.OrdinalIgnoreCase) && !columns.ContainsKey(req))
                {
                    return $"'table.requiredOnStartedRow': '{req}' no es una columna ni el token '@first'.";
                }
            }
        }

        return null;
    }

    /// <summary>Resuelve el juego de columnas efectivo de una opcion (por id) de un nivel que abre tabla:
    /// <c>option.columnSet</c> -&gt; columnSet de la opcion PADRE (recursivo) -&gt;
    /// <c>DefaultColumnSet</c> del nivel. Null si no resuelve. Es la MISMA logica que valida el contrato;
    /// el renderer debe reusar este metodo para no divergir.</summary>
    public static string? ResolveColumnSet(CascadeConfig cfg, string optionId)
    {
        var index = BuildOptionIndex(cfg);
        return index.TryGetValue(optionId, out var entry)
            ? ResolveColumnSet(entry.Option, entry.Level, index)
            : null;
    }

    private static string? ResolveColumnSet(
        CascadeOption option,
        CascadeLevel level,
        IReadOnlyDictionary<string, (CascadeOption Option, CascadeLevel Level)> index)
    {
        var current = option;
        var guard = 0;
        while (current is not null && guard++ < 50)
        {
            if (!string.IsNullOrWhiteSpace(current.ColumnSet))
            {
                return current.ColumnSet;
            }
            if (string.IsNullOrWhiteSpace(current.Parent))
            {
                break;
            }
            current = index.TryGetValue(current.Parent, out var p) ? p.Option : null;
        }
        return level.DefaultColumnSet;
    }

    private static Dictionary<string, (CascadeOption Option, CascadeLevel Level)> BuildOptionIndex(CascadeConfig cfg)
    {
        var map = new Dictionary<string, (CascadeOption Option, CascadeLevel Level)>(StringComparer.Ordinal);
        foreach (var lvl in cfg.Levels)
        {
            if (lvl.Options is null)
            {
                continue;
            }
            foreach (var op in lvl.Options)
            {
                map[op.Id] = (op, lvl);
            }
        }

        return map;
    }
}

/// <summary>Un nivel de la cascada. Se encadenan por <see cref="DependsOn"/> (hacia atras).</summary>
public sealed record CascadeLevel(
    string Key,
    string Label,
    string Select = "multi",
    string? DependsOn = null,
    bool OpensTable = false,
    string? FeedsColumn = null,
    // Pista informativa del origen del juego de columnas. La resolucion REAL es por opcion
    // (option.columnSet -> padre -> DefaultColumnSet), no depende de este campo.
    string? ColumnSetFrom = null,
    // Ultimo fallback del juego de columnas cuando una opcion no lo define ni lo hereda del padre.
    string? DefaultColumnSet = null,
    IReadOnlyList<CascadeOption>? Options = null,
    CascadeSource? Source = null);

/// <summary>Opcion inline de un nivel. <see cref="Parent"/> la ata a una opcion del nivel padre;
/// <see cref="ColumnSet"/> (en niveles con tabla) define su juego de columnas.</summary>
public sealed record CascadeOption(
    string Id,
    string Label,
    string? Parent = null,
    string? ColumnSet = null,
    string? Color = null,
    string? Desc = null);

/// <summary>Fuente externa de opciones de un nivel (reusa el lookup del sistema). Los *Field mapean
/// las columnas de la fuente a id/label/parent/columnSet de la opcion.</summary>
public sealed record CascadeSource(
    FormSourceKind Kind,
    string? Ref = null,
    string? ValueField = null,
    string? DisplayField = null,
    string? ParentField = null,
    string? ColumnSetField = null,
    string? FilterJson = null);

/// <summary>Definicion de una columna de tabla. Mapea 1:1 al <c>FormGridColumn</c> de GridDetail, de
/// modo que el calculo corre por el pipeline existente (FormExpressionEvaluator / FormGridCalculator).</summary>
public sealed record CascadeColumn(
    string Label,
    string Kind = "text",        // text | number | money | select
    string? Width = null,        // ancho CSS tal cual el prototipo: "78px", "118px", "auto"...
    string? Placeholder = null,
    string? Format = null,        // currency | percent | integer | decimal | document
    string? Calc = null,          // formula por fila (mismo lenguaje que el cotizador)
    string? Agg = null,           // None | Sum | Count | Avg | Min | Max
    string? Rollup = null,        // formula de roll-up al encabezado
    string? AggWhen = null,       // condicion para excluir filas del agregado
    bool Required = false,
    IReadOnlyList<string>? Suggest = null);   // sugerencias tipo datalist (no restringe)

/// <summary>Regla de armado de tablas. <see cref="KeyBy"/> = por que se agrupa una tabla (niveles y/o
/// la palabra 'columnSet'); <see cref="RequiredOnStartedRow"/> = columnas obligatorias en una fila ya
/// iniciada al guardar.</summary>
public sealed record CascadeTable(
    IReadOnlyList<string> KeyBy,
    IReadOnlyList<string>? RequiredOnStartedRow = null);

/// <summary>
/// DOCUMENTACION del shape que el renderer guarda en <c>FormResponse.Data</c> (jsonb) al capturar. Se
/// deja como referencia del contrato de persistencia (consultable/reportable como cualquier formulario):
/// <code>
/// {
///   "selections": { "&lt;levelKey&gt;": "id" | ["id", ...] },
///   "tables": [
///     { "key": "g1#sap", "keyBy": {"portafolio":"g1","columnSet":"sap"},
///       "branchLabels": ["SAP"], "feed": {"proceso": ["laser"]},
///       "rows": [ {"&lt;colKey&gt;": "valor", "&lt;calcColKey&gt;": 3000 } ],
///       "rollups": {"&lt;colKey&gt;": 3000} }
///   ],
///   "totals": { "&lt;colKey&gt;": 3000 }
/// }
/// </code>
/// </summary>
public static class CascadeResultShape
{
    public const string SchemaVersion = "1";
}

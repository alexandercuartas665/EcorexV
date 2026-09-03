using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Reporting.Panels;

// ADR-0066 - Renderizador de panel GENERICO por spec. El PanelSpec es el DATO que reemplaza a un
// componente Blazor compilado: describe fuentes, join/lookup, campos derivados, filtros, KPIs y
// widgets. Referencia SOLO nombres de negocio del catalogo tenant-safe (DisplayName de la fuente),
// alias traidos por lookup y nombres de campos derivados. No lleva SQL ni columnas fisicas. Un solo
// componente (SpecPanelRenderer) lo interpreta: una consulta tabular por fuente + pivoteo en memoria.
//
// Enums viajan como texto (agg, format, op, control, type) para que el editor JSON sea amable y el
// esquema sea estable frente a reordenamientos.

/// <summary>La definicion declarativa de un panel (dashboard multi-grafico) como DATO.</summary>
public sealed class PanelSpec
{
    /// <summary>Titulo del panel (informativo; la galeria muestra el Name del ReportDefinition).</summary>
    public string Title { get; set; } = "";

    /// <summary>Fuentes: la principal (una consulta) + lookups para join en memoria.</summary>
    public PanelSources Sources { get; set; } = new();

    /// <summary>Join en memoria de la fuente principal con un lookup (codigo -> nombre).</summary>
    public PanelJoin? Join { get; set; }

    /// <summary>Campos derivados en memoria (buckets de fecha: year / yyyymm / month / date).</summary>
    public List<PanelDerived> Derived { get; set; } = new();

    /// <summary>Filtros FIJOS del spec (ADR-0068): se aplican SIEMPRE, no son controles de UI. Acotan el
    /// panel a un subconjunto (p.ej. Tablero eq "GESTION COMERCIAL"). Ops: eq | ne | contains | gt | gte |
    /// lt | lte. Se evaluan tras el join/derivados y antes de poblar los dropdowns.</summary>
    public List<PanelWhere> Where { get; set; } = new();

    /// <summary>Filtros que se auto-pueblan (distinct) o por tipo (dropdown / daterange / text).</summary>
    public List<PanelFilter> Filters { get; set; } = new();

    /// <summary>Indicadores clave: agregacion + campo + formato.</summary>
    public List<PanelKpi> Kpis { get; set; } = new();

    /// <summary>Graficos y tablas del panel.</summary>
    public List<PanelWidget> Widgets { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializa un PanelSpec. Devuelve null si el JSON es invalido (no lanza).</summary>
    public static PanelSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PanelSpec>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class PanelSources
{
    /// <summary>Fuente principal (una sola consulta tabular).</summary>
    public PanelSource Main { get; set; } = new();

    /// <summary>Lookups para enriquecer en memoria (codigo -> nombre).</summary>
    public List<PanelLookup> Lookups { get; set; } = new();
}

public sealed class PanelSource
{
    /// <summary>Nombre de negocio de la fuente en el catalogo del tenant (DisplayName: un contenedor o
    /// una entidad nativa como "Actividades"). El renderizador la resuelve por nombre en el tenant.</summary>
    public string Container { get; set; } = "";

    /// <summary>Alternativa/preferente a <see cref="Container"/>: la CLAVE de la fuente en el catalogo
    /// (ej. "native:taskitem", "form:COT", "container:{guid}"). Estable entre entornos (util para modulos:
    /// el code no cambia, el titulo si). Si viene, se resuelve por clave; si no, por DisplayName.</summary>
    public string? Source { get; set; }
}

public sealed class PanelLookup
{
    /// <summary>Nombre de negocio del contenedor/entidad de lookup en el catalogo.</summary>
    public string Container { get; set; } = "";

    /// <summary>Alternativa/preferente a <see cref="Container"/>: la CLAVE de la fuente (ej. "form:COT").</summary>
    public string? Source { get; set; }

    /// <summary>Campo de la fuente PRINCIPAL que cruza con <see cref="Key"/> de ESTE lookup (DisplayName o
    /// Key del campo). Deja cada lookup autocontenido (MainKey -> Key, trae Bring), permitiendo 2+ lookups.
    /// Si falta, por compatibilidad se usa <see cref="PanelJoin.MainKey"/> cuando <see cref="PanelJoin.Lookup"/>
    /// apunta a este lookup (asi el reporte SIIGO actual, que solo usa Join, no se rompe).</summary>
    public string? MainKey { get; set; }

    /// <summary>Campo clave del lookup con el que se cruza (DisplayName o Key del campo).</summary>
    public string Key { get; set; } = "";

    /// <summary>Normalizacion de la clave del lookup antes de comparar con <see cref="MainKey"/> (ADR-0068).
    /// "beforeDash" toma lo anterior al primer '-' (ej. "T00016-1" -> "T00016"), para cruzar el numero de
    /// tarea con la referencia de una respuesta de formulario. Vacio = sin transformacion.</summary>
    public string? KeyTransform { get; set; }

    /// <summary>Dedupe del lookup antes de cruzar (ADR-0068): si una clave tiene varias filas (revisiones),
    /// conserva una sola. Ej. { By: "Reference", Keep: "latest" } se queda con la mas reciente.</summary>
    public PanelReduce? Reduce { get; set; }

    /// <summary>Campos a traer del lookup: campoOrigen -> aliasDestino (nombre logico en la fila). El origen
    /// puede referirse al DisplayName o al Key del campo. Un alias numerico se puede AGREGAR (Sum) despues.</summary>
    public Dictionary<string, string> Bring { get; set; } = new();
}

/// <summary>Dedupe de un lookup por una clave, conservando una fila (ADR-0068).</summary>
public sealed class PanelReduce
{
    /// <summary>Campo del lookup por el que se agrupa para deduplicar (DisplayName o Key).</summary>
    public string By { get; set; } = "";

    /// <summary>Cual conservar por grupo: "latest" (mayor fecha) | "first" (la primera vista). Default latest.</summary>
    public string Keep { get; set; } = "latest";
}

/// <summary>Filtro FIJO del spec (ADR-0068): campo + operador + valor. No es un control de UI.</summary>
public sealed class PanelWhere
{
    /// <summary>Campo sobre el que aplica (de la fuente, un alias de lookup o un derivado).</summary>
    public string Field { get; set; } = "";

    /// <summary>Operador: eq | ne | contains | gt | gte | lt | lte.</summary>
    public string Op { get; set; } = "eq";

    /// <summary>Valor de comparacion (texto; para gt/lt se interpreta numerico o fecha si aplica).</summary>
    public string? Value { get; set; }
}

public sealed class PanelJoin
{
    /// <summary>Campo de la fuente principal que hace de clave del cruce (DisplayName).</summary>
    public string MainKey { get; set; } = "";

    /// <summary>Nombre del lookup (coincide con PanelLookup.Container) contra el que se cruza.</summary>
    public string Lookup { get; set; } = "";
}

public sealed class PanelDerived
{
    /// <summary>Nombre logico del campo derivado (se usa en filtros/widgets como cualquier campo).</summary>
    public string Name { get; set; } = "";

    /// <summary>Campo de fecha de origen (DisplayName de la fuente principal).</summary>
    public string From { get; set; } = "";

    /// <summary>Operacion: year | yyyymm | month | date.</summary>
    public string Op { get; set; } = "";
}

public sealed class PanelFilter
{
    /// <summary>Campo sobre el que filtra (de la fuente, un alias de lookup o un derivado).</summary>
    public string Field { get; set; } = "";

    /// <summary>Control: dropdown (distinct auto) | daterange | text (contiene).</summary>
    public string Control { get; set; } = "dropdown";

    /// <summary>Etiqueta opcional; si falta se usa el nombre del campo.</summary>
    public string? Label { get; set; }
}

public sealed class PanelKpi
{
    public string Label { get; set; } = "";

    /// <summary>Agregacion: sum | count | countDistinct | avg.</summary>
    public string Agg { get; set; } = "count";

    /// <summary>Campo objeto de la agregacion (no requerido para count).</summary>
    public string? Field { get; set; }

    /// <summary>Formato: money | moneyM | percent | int.</summary>
    public string Format { get; set; } = "int";

    /// <summary>KPI CONDICIONAL opcional (ADR-0068): agrega SOLO las filas que cumplen estas condiciones
    /// (todas, AND). Vacio = sobre todas las filas del panel.</summary>
    public List<PanelWhere> When { get; set; } = new();
}

public sealed class PanelWidget
{
    /// <summary>Tipo: line | bar | donut | pareto | matrix | table.</summary>
    public string Type { get; set; } = "bar";

    public string Title { get; set; } = "";

    /// <summary>Dimension (group by) para line/bar/donut/pareto.</summary>
    public string? Dim { get; set; }

    /// <summary>Agregacion de la medida: sum | count | countDistinct | avg.</summary>
    public string? Agg { get; set; }

    /// <summary>Campo de la medida (no requerido para count).</summary>
    public string? Field { get; set; }

    /// <summary>Limite de categorias (top N por medida). Null = todas.</summary>
    public int? TopN { get; set; }

    /// <summary>Divisor de la medida (p.ej. 1000000 para "millones").</summary>
    public double? Scale { get; set; }

    /// <summary>bar: horizontal | vertical (por defecto vertical).</summary>
    public string? Orientation { get; set; }

    /// <summary>pareto: dibuja la linea de acumulado %.</summary>
    public bool Cumulative { get; set; }

    /// <summary>matrix: pinta heatmap de intensidad por celda.</summary>
    public bool Heatmap { get; set; }

    /// <summary>matrix: dimension de fila.</summary>
    public string? RowDim { get; set; }

    /// <summary>matrix: dimension de columna.</summary>
    public string? ColDim { get; set; }

    /// <summary>Ancho en la grilla: full | half (por defecto half).</summary>
    public string? Width { get; set; }

    /// <summary>line: orden de la dimension (asc por defecto para series temporales).</summary>
    public string? SortDim { get; set; }

    /// <summary>table: campo por el que se agrupan las filas.</summary>
    public string? GroupBy { get; set; }

    /// <summary>Formato de la etiqueta de valor del widget (money | moneyM | percent | int).</summary>
    public string? Format { get; set; }

    /// <summary>Alto en px del grafico (opcional; el renderizador tiene un default por tipo).</summary>
    public int? Height { get; set; }

    /// <summary>table: columnas computadas.</summary>
    public List<PanelColumn> Columns { get; set; } = new();
}

public sealed class PanelColumn
{
    public string Label { get; set; } = "";

    /// <summary>Campo directo a mostrar (primer valor del grupo). Excluyente con Agg.</summary>
    public string? Field { get; set; }

    /// <summary>Agregacion sobre el grupo: sum | count | countDistinct | avg.</summary>
    public string? Agg { get; set; }

    /// <summary>Campo de la agregacion (no requerido para count).</summary>
    public string? AggField { get; set; }

    /// <summary>Formato numerico: money | moneyM | percent | int.</summary>
    public string? Format { get; set; }
}

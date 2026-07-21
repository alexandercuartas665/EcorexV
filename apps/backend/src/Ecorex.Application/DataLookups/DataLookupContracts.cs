using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataLookups;

// Motor COMPARTIDO de campos tipo lista alimentados por el Contenedor de datos.
// Vive fuera de Forms a proposito: lo consumen los campos configurables del tercero
// (000232), los del item (000066) y, mas adelante, el propio motor de formularios.
// Nada aqui conoce una tabla concreta: todo se resuelve por Id en tiempo de ejecucion.

// ---- Catalogo para el configurador: modelo -> tabla -> columna ----

public sealed record LookupModelDto(Guid Id, string Name, int TableCount);

public sealed record LookupTableDto(Guid Id, string Name, Guid? ModelId, string? ModelName);

public sealed record LookupColumnDto(Guid Id, string Name, DataContainerColumnType Type, int SortOrder);

// ---- Configuracion de un campo tipo lista (la serializan tercero/item/formularios) ----

/// <summary>
/// Un filtro sobre la tabla origen. O trae un <paramref name="Value"/> fijo (definido al
/// configurar), o toma su valor de OTRO campo del formulario via <paramref name="FromFieldKey"/>
/// (cascada). Los dos a la vez no tienen sentido: si hay FromFieldKey, Value se ignora y sirve
/// solo como respaldo cuando el campo origen esta vacio.
///
/// FromFieldKey referencia un campo de TODO el formulario, no solo del bloque donde vive el
/// campo. En el tercero los campos se agrupan por ficha y la clave solo es unica DENTRO de su
/// ficha (puede haber dos "ciudad"), asi que ahi la referencia se cualifica como "ficha/campo";
/// en items no hace falta porque un item tiene un solo tipo. El motor no interpreta la clave:
/// solo la busca tal cual en el diccionario de valores que le pase quien llama.
/// </summary>
/// <param name="RequireSource">
/// true = mientras el campo origen este vacio, la lista no devuelve NADA (obliga a elegir
/// primero el campo padre). false (por defecto) = el filtro se omite y se ve la lista completa.
/// </param>
public sealed record DataLookupFilterConfig(
    Guid ColumnId,
    string? Value = null,
    string? FromFieldKey = null,
    bool RequireSource = false);

/// <summary>Al elegir una fila, el valor de <paramref name="ColumnId"/> se copia al campo destino.</summary>
public sealed record DataLookupAutofillConfig(Guid ColumnId, string TargetFieldKey);

/// <summary>
/// Configuracion completa de un campo tipo lista. Se guarda serializada como JSON en el mismo
/// sitio donde cada modulo guarda hoy sus opciones de texto, asi que NO requiere columnas nuevas.
///
/// Las columnas se referencian por ID, no por nombre: renombrar una columna en el Contenedor
/// no debe romper la configuracion en silencio (que es lo que pasa hoy en formularios, donde
/// DisplayField y AutofillMapJson van por nombre). Los nombres se conservan aparte, solo para
/// poder mostrar algo legible si la columna desaparece.
/// </summary>
public sealed record DataLookupConfig(
    Guid TableId,
    Guid? ModelId = null,
    Guid? DisplayColumnId = null,
    IReadOnlyList<DataLookupFilterConfig>? Filters = null,
    IReadOnlyList<DataLookupAutofillConfig>? Autofill = null,
    string? TableName = null,
    string? DisplayColumnName = null)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// Lee la configuracion desde el texto guardado. Devuelve null si no es un JSON de lookup
    /// (p.ej. las opciones de toda la vida, una por linea): asi un campo Select existente sigue
    /// funcionando igual y la deteccion no depende de ninguna bandera aparte.
    /// </summary>
    public static DataLookupConfig? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var texto = raw.TrimStart();
        if (texto.Length == 0 || texto[0] != '{') { return null; }
        try
        {
            var cfg = JsonSerializer.Deserialize<DataLookupConfig>(texto, JsonOpts);
            return cfg is null || cfg.TableId == Guid.Empty ? null : cfg;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ---- Consulta en tiempo de llenado ----

/// <param name="Filters">Valores YA resueltos por columna. Quien llama decide de donde salen
/// (fijos o tomados de otro campo); el motor no conoce el formulario.</param>
/// <param name="ExtraColumnIds">Columnas adicionales a traer, para el autollenado.</param>
public sealed record DataLookupQuery(
    Guid TableId,
    string? Search = null,
    Guid? DisplayColumnId = null,
    IReadOnlyDictionary<Guid, string>? Filters = null,
    IReadOnlyList<Guid>? ExtraColumnIds = null,
    int Page = 1,
    int PageSize = 20);

public sealed record DataLookupRowDto(Guid RowId, string Label, IReadOnlyDictionary<Guid, string?> Values);

public sealed record DataLookupPageDto(IReadOnlyList<DataLookupRowDto> Rows, int Total, int Page, int PageSize);

/// <summary>
/// Motor de listas alimentadas por el Contenedor de datos. Tenant-scoped por el filtro global:
/// una tabla de otro tenant sencillamente no existe para estas consultas.
/// </summary>
public interface IDataLookupService
{
    Task<IReadOnlyList<LookupModelDto>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Tablas de un modelo, o TODAS las tablas raiz si no se indica modelo.</summary>
    Task<IReadOnlyList<LookupTableDto>> ListTablesAsync(Guid? modelId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupColumnDto>> ListColumnsAsync(Guid tableId, CancellationToken cancellationToken = default);

    /// <summary>Busca filas para el selector. Delega la consulta paginada al Contenedor de datos.</summary>
    Task<DataLookupPageDto> SearchAsync(DataLookupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve filas YA guardadas (por su Id) para poder mostrar su etiqueta. Es lo que hace
    /// util la "referencia viva": el registro guarda el Id y la etiqueta se lee al mostrar, de
    /// modo que corregir el dato en el Contenedor se refleja en todas partes.
    /// Las filas que ya no existan simplemente no vuelven.
    /// </summary>
    Task<IReadOnlyList<DataLookupRowDto>> ResolveAsync(
        Guid tableId,
        IReadOnlyList<Guid> rowIds,
        Guid? displayColumnId = null,
        IReadOnlyList<Guid>? extraColumnIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busqueda a partir de la CONFIGURACION del campo y del estado del formulario: resuelve la
    /// cascada y consulta en un solo paso. Es lo que llama la UI, para que el cableado de
    /// filtros no se repita (ni se desvie) en cada modulo.
    /// Si un filtro obligatorio no tiene su campo origen lleno, devuelve pagina VACIA sin ir a
    /// la base: no hay nada que consultar todavia.
    /// </summary>
    Task<DataLookupPageDto> SearchForFieldAsync(
        DataLookupConfig config,
        IReadOnlyDictionary<string, string?>? formValues,
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}

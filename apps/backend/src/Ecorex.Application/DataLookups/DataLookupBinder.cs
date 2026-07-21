namespace Ecorex.Application.DataLookups;

/// <summary>
/// Resultado de resolver los filtros de un campo tipo lista contra el estado del formulario.
/// </summary>
/// <param name="Blocked">
/// true = hay un filtro con RequireSource cuyo campo origen esta vacio, asi que la lista NO debe
/// consultarse todavia (se muestra vacia con su motivo). Es la diferencia entre "no hay
/// resultados" y "primero elige el otro campo", que si se confunden el usuario no sabe que hacer.
/// </param>
/// <param name="MissingSources">Claves de los campos origen que faltan por llenar.</param>
public sealed record DataLookupFilterResolution(
    IReadOnlyDictionary<Guid, string> Filters,
    bool Blocked,
    IReadOnlyList<string> MissingSources);

/// <summary>
/// Logica de CASCADA y AUTOLLENADO, sin dependencias de base de datos ni de un modulo concreto.
///
/// El motor (<see cref="IDataLookupService"/>) solo recibe valores ya resueltos; quien decide de
/// donde salen es esto. Se mantiene aparte y sin estado para que sirva igual al tercero (que
/// agrupa sus campos por ficha), al item y al motor de formularios, y para poder probar la
/// mecanica de la cascada sin levantar contenedores.
/// </summary>
public static class DataLookupBinder
{
    /// <summary>
    /// Campos de los que DEPENDE este campo tipo lista. La UI los usa para saber cuando recargar
    /// las opciones: si cambia alguno de estos, la lista ya no es la misma.
    /// </summary>
    public static IReadOnlyList<string> DependenciesOf(DataLookupConfig config)
    {
        if (config.Filters is null) { return []; }
        return config.Filters
            .Select(f => f.FromFieldKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Traduce los filtros configurados a valores concretos usando el estado actual del
    /// formulario. Un filtro con FromFieldKey toma el valor de ese campo; si esta vacio, cae a
    /// Value como respaldo y, si tampoco hay, se omite (o bloquea la lista si es RequireSource).
    /// </summary>
    public static DataLookupFilterResolution ResolveFilters(
        DataLookupConfig config,
        IReadOnlyDictionary<string, string?>? formValues)
    {
        var filtros = new Dictionary<Guid, string>();
        var faltantes = new List<string>();
        var bloqueado = false;

        foreach (var f in config.Filters ?? [])
        {
            string? valor = null;

            if (!string.IsNullOrWhiteSpace(f.FromFieldKey))
            {
                if (formValues is not null
                    && formValues.TryGetValue(f.FromFieldKey!, out var v)
                    && !string.IsNullOrWhiteSpace(v))
                {
                    valor = v;
                }
                else if (!string.IsNullOrWhiteSpace(f.Value))
                {
                    valor = f.Value;
                }
                else
                {
                    faltantes.Add(f.FromFieldKey!);
                    if (f.RequireSource) { bloqueado = true; }
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(f.Value))
            {
                valor = f.Value;
            }
            else
            {
                // Filtro sin valor fijo ni origen: no dice nada, se ignora.
                continue;
            }

            // Dos filtros sobre la MISMA columna no se pueden cumplir a la vez (el ultimo
            // ganaria en silencio), asi que se conserva el primero, que es el que el
            // configurador ve arriba en la lista.
            filtros.TryAdd(f.ColumnId, valor!);
        }

        return new DataLookupFilterResolution(filtros, bloqueado, faltantes);
    }

    /// <summary>
    /// Columnas que hay que traer de la fila ademas de la que se muestra: las que alimentan el
    /// autollenado. Si no hay autollenado devuelve null = "trae la fila completa".
    /// </summary>
    public static IReadOnlyList<Guid>? ColumnsNeeded(DataLookupConfig config)
    {
        if (config.Autofill is null || config.Autofill.Count == 0) { return null; }
        var ids = config.Autofill.Select(a => a.ColumnId).ToList();
        foreach (var f in config.Filters ?? [])
        {
            // Las columnas filtradas tambien se traen: sirven para revalidar sin otra consulta.
            ids.Add(f.ColumnId);
        }
        return ids.Distinct().ToList();
    }

    /// <summary>
    /// Valores a volcar en otros campos al elegir una fila. Devuelve destino -> valor.
    /// Una columna que ya no exista en la fila entrega null, para que el campo destino se limpie
    /// en vez de quedarse con el dato de la seleccion anterior (que seria peor: un valor que ya
    /// no corresponde a lo elegido).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> BuildAutofill(
        DataLookupConfig config, DataLookupRowDto? row)
    {
        var salida = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var a in config.Autofill ?? [])
        {
            if (string.IsNullOrWhiteSpace(a.TargetFieldKey)) { continue; }
            salida[a.TargetFieldKey] = row is not null && row.Values.TryGetValue(a.ColumnId, out var v)
                ? v
                : null;
        }
        return salida;
    }

    /// <summary>
    /// Comprueba si la fila YA elegida sigue cumpliendo los filtros vigentes. Se usa cuando
    /// cambia un campo padre: si la seleccion dejo de ser valida hay que limpiarla, porque
    /// dejarla produce combinaciones incoherentes (un item de un grupo que ya no es el elegido).
    /// Compara igual que la consulta: sin distinguir mayusculas.
    /// </summary>
    public static bool StillMatches(
        DataLookupRowDto row, IReadOnlyDictionary<Guid, string> resolvedFilters)
    {
        foreach (var (columnId, esperado) in resolvedFilters)
        {
            if (!row.Values.TryGetValue(columnId, out var actual)) { return false; }
            if (actual is null) { return false; }
            if (!actual.Contains(esperado, StringComparison.OrdinalIgnoreCase)) { return false; }
        }
        return true;
    }
}

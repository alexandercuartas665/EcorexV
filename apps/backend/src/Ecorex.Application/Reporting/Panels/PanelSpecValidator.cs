namespace Ecorex.Application.Reporting.Panels;

// Validador del PanelSpec contra el catalogo tenant-safe (ADR-0066). Es el limite de seguridad de la
// AUTORIA: un spec solo puede referenciar fuentes y campos de NEGOCIO que existen en el catalogo del
// tenant (mas los alias traidos por lookup y los campos derivados que el propio spec declara). No toca
// la BD: valida sobre los descriptores ya publicados por IReportCatalog. Devuelve mensajes claros para
// que el editor los muestre. Puro y testeable.

public static class PanelSpecValidator
{
    private static readonly HashSet<string> KnownWidgets =
        new(new[] { "line", "bar", "donut", "pareto", "matrix", "table" }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownAggs =
        new(new[] { "sum", "count", "countdistinct", "avg" }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownFormats =
        new(new[] { "money", "moneym", "percent", "int" }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownDerivedOps =
        new(new[] { "year", "yyyymm", "month", "date" }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownControls =
        new(new[] { "dropdown", "daterange", "text" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>Valida el spec contra las fuentes del catalogo. Lista vacia = valido.</summary>
    public static IReadOnlyList<string> Validate(PanelSpec spec, IReadOnlyList<ReportSourceDescriptor> catalog)
    {
        var errors = new List<string>();

        if (spec is null)
        {
            return new[] { "El PanelSpec es nulo o no se pudo interpretar como JSON." };
        }

        // 1) Fuente principal.
        var mainName = spec.Sources?.Main?.Container;
        if (string.IsNullOrWhiteSpace(mainName))
        {
            errors.Add("sources.main.container es obligatorio (nombre de la fuente principal).");
            return errors;
        }

        var main = FindSource(catalog, mainName!);
        if (main is null)
        {
            errors.Add($"La fuente principal '{mainName}' no existe en el catalogo de este tenant.");
            return errors;
        }

        var mainFields = new HashSet<string>(main.Fields.Select(f => f.DisplayName), StringComparer.OrdinalIgnoreCase);

        // 2) Lookups + campos traidos (alias disponibles).
        var available = new HashSet<string>(mainFields, StringComparer.OrdinalIgnoreCase);
        var lookupsByName = new Dictionary<string, ReportSourceDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var lk in spec.Sources?.Lookups ?? new List<PanelLookup>())
        {
            if (string.IsNullOrWhiteSpace(lk.Container))
            {
                errors.Add("Un lookup no tiene 'container'.");
                continue;
            }

            var src = FindSource(catalog, lk.Container);
            if (src is null)
            {
                errors.Add($"El lookup '{lk.Container}' no existe en el catalogo de este tenant.");
                continue;
            }

            lookupsByName[lk.Container] = src;
            var lkFields = new HashSet<string>(src.Fields.Select(f => f.DisplayName), StringComparer.OrdinalIgnoreCase);

            // MainKey autocontenido del lookup; si falta, cae al Join (compatibilidad) cuando Join.Lookup
            // apunta a este lookup. Sin ninguno de los dos, el lookup no se aplicaria.
            var effMainKey = !string.IsNullOrWhiteSpace(lk.MainKey)
                ? lk.MainKey
                : (spec.Join is not null
                    && string.Equals(spec.Join.Lookup, lk.Container, StringComparison.OrdinalIgnoreCase)
                        ? spec.Join.MainKey
                        : null);
            if (string.IsNullOrWhiteSpace(effMainKey))
            {
                errors.Add($"El lookup '{lk.Container}' no declara 'mainKey' y no hay un join que lo cruce: no se aplicaria.");
            }
            else if (!mainFields.Contains(effMainKey))
            {
                errors.Add($"El lookup '{lk.Container}' usa mainKey '{effMainKey}', que no es un campo de la fuente principal.");
            }

            if (!string.IsNullOrWhiteSpace(lk.Key) && !lkFields.Contains(lk.Key))
            {
                errors.Add($"El lookup '{lk.Container}' no tiene el campo clave '{lk.Key}'.");
            }

            foreach (var bring in lk.Bring)
            {
                if (!lkFields.Contains(bring.Key))
                {
                    errors.Add($"El lookup '{lk.Container}' no tiene el campo '{bring.Key}' que se intenta traer.");
                }

                if (!string.IsNullOrWhiteSpace(bring.Value))
                {
                    // El alias no debe pisar un campo ya disponible (de la principal o de otro lookup): una
                    // colision silenciosa mezclaria datos de dos fuentes bajo el mismo nombre logico.
                    if (!available.Add(bring.Value))
                    {
                        errors.Add($"El alias '{bring.Value}' del lookup '{lk.Container}' choca con un campo ya existente (fuente principal u otro lookup).");
                    }
                }
            }
        }

        // 3) Join.
        if (spec.Join is not null)
        {
            if (!string.IsNullOrWhiteSpace(spec.Join.MainKey) && !mainFields.Contains(spec.Join.MainKey))
            {
                errors.Add($"join.mainKey '{spec.Join.MainKey}' no es un campo de la fuente principal.");
            }

            if (!string.IsNullOrWhiteSpace(spec.Join.Lookup) && !lookupsByName.ContainsKey(spec.Join.Lookup))
            {
                errors.Add($"join.lookup '{spec.Join.Lookup}' no coincide con ningun lookup declarado.");
            }
        }

        // 4) Derivados (buckets de fecha) -> agregan nombres disponibles.
        foreach (var d in spec.Derived)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
            {
                errors.Add("Un campo derivado no tiene 'name'.");
                continue;
            }

            if (!mainFields.Contains(d.From))
            {
                errors.Add($"El derivado '{d.Name}' usa 'from' = '{d.From}', que no es un campo de la fuente principal.");
            }
            else
            {
                var field = main.Fields.First(f => string.Equals(f.DisplayName, d.From, StringComparison.OrdinalIgnoreCase));
                if (field.Type != ReportFieldType.Date)
                {
                    errors.Add($"El derivado '{d.Name}' exige un campo de fecha; '{d.From}' es {field.Type}.");
                }
            }

            if (!KnownDerivedOps.Contains(d.Op ?? ""))
            {
                errors.Add($"El derivado '{d.Name}' tiene una operacion desconocida: '{d.Op}' (year|yyyymm|month|date).");
            }

            available.Add(d.Name);
        }

        // 5) Filtros.
        foreach (var f in spec.Filters)
        {
            if (!available.Contains(f.Field))
            {
                errors.Add($"El filtro sobre '{f.Field}' no es un campo disponible (fuente, alias de lookup o derivado).");
            }

            if (!KnownControls.Contains(f.Control ?? ""))
            {
                errors.Add($"El filtro sobre '{f.Field}' tiene un control desconocido: '{f.Control}' (dropdown|daterange|text).");
            }
        }

        // 6) KPIs.
        foreach (var k in spec.Kpis)
        {
            if (!KnownAggs.Contains(k.Agg ?? ""))
            {
                errors.Add($"El KPI '{k.Label}' tiene una agregacion desconocida: '{k.Agg}'.");
            }

            RequireMeasureField(errors, $"El KPI '{k.Label}'", k.Agg, k.Field, available);

            if (!KnownFormats.Contains(k.Format ?? ""))
            {
                errors.Add($"El KPI '{k.Label}' tiene un formato desconocido: '{k.Format}' (money|moneyM|percent|int).");
            }
        }

        // 7) Widgets.
        foreach (var w in spec.Widgets)
        {
            var label = string.IsNullOrWhiteSpace(w.Title) ? w.Type : w.Title;
            if (!KnownWidgets.Contains(w.Type ?? ""))
            {
                errors.Add($"El widget '{label}' tiene un tipo desconocido: '{w.Type}'.");
                continue;
            }

            switch (w.Type!.ToLowerInvariant())
            {
                case "matrix":
                    RequireField(errors, $"El widget '{label}'", "rowDim", w.RowDim, available);
                    RequireField(errors, $"El widget '{label}'", "colDim", w.ColDim, available);
                    RequireMeasureField(errors, $"El widget '{label}'", w.Agg, w.Field, available);
                    break;
                case "table":
                    RequireField(errors, $"El widget '{label}'", "groupBy", w.GroupBy, available);
                    if (w.Columns.Count == 0)
                    {
                        errors.Add($"El widget tabla '{label}' no declara columnas.");
                    }

                    foreach (var col in w.Columns)
                    {
                        if (!string.IsNullOrWhiteSpace(col.Agg))
                        {
                            RequireMeasureField(errors, $"La columna '{col.Label}'", col.Agg, col.AggField, available);
                        }
                        else if (!string.IsNullOrWhiteSpace(col.Field) && !available.Contains(col.Field))
                        {
                            errors.Add($"La columna '{col.Label}' usa el campo '{col.Field}', que no esta disponible.");
                        }
                    }

                    break;
                default: // line | bar | donut | pareto
                    RequireField(errors, $"El widget '{label}'", "dim", w.Dim, available);
                    RequireMeasureField(errors, $"El widget '{label}'", w.Agg, w.Field, available);
                    break;
            }
        }

        return errors;
    }

    private static void RequireField(List<string> errors, string who, string prop, string? value, HashSet<string> available)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{who} requiere '{prop}'.");
        }
        else if (!available.Contains(value))
        {
            errors.Add($"{who} usa {prop} = '{value}', que no es un campo disponible.");
        }
    }

    private static void RequireMeasureField(List<string> errors, string who, string? agg, string? field, HashSet<string> available)
    {
        var op = (agg ?? "count").Trim().ToLowerInvariant();
        // count no exige campo; el resto si.
        if (op == "count")
        {
            if (!string.IsNullOrWhiteSpace(field) && !available.Contains(field))
            {
                errors.Add($"{who} referencia el campo '{field}', que no esta disponible.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            errors.Add($"{who} usa la agregacion '{agg}', que exige un campo.");
        }
        else if (!available.Contains(field))
        {
            errors.Add($"{who} usa el campo '{field}', que no esta disponible.");
        }
    }

    /// <summary>Resuelve una fuente por su nombre de negocio (DisplayName) entre nativas y contenedores.
    /// Preferencia: coincidencia exacta; luego case-insensitive. Ignora fuentes externas (no filtrables).</summary>
    public static ReportSourceDescriptor? FindSource(IReadOnlyList<ReportSourceDescriptor> catalog, string name)
    {
        var candidates = catalog.Where(s => s.Kind != ReportSourceKind.External).ToList();
        return candidates.FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));
    }
}

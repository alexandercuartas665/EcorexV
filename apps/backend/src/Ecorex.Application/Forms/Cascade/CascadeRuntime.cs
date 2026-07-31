namespace Ecorex.Application.Forms.Cascade;

/// <summary>
/// Logica PURA (sin UI) del motor del configurador en cascada: dado un <see cref="CascadeConfig"/> y lo
/// que el usuario lleva seleccionado, calcula que opciones se ven en cada nivel, que tablas se deben
/// mostrar (una por rama + juego de columnas), y que valores "alimentan" columnas (ej. procesos ->
/// columna "proceso"). El renderer Blazor consume esto y solo se ocupa del estado y el render; el calculo
/// de filas/totales lo hace FormGridCalculator. Reproduce las reglas del prototipo SOLDARCO de forma
/// generica (cualquier taxonomia).
/// </summary>
public static class CascadeRuntime
{
    /// <summary>Opciones visibles de un nivel segun lo seleccionado: si el nivel depende de otro, solo se
    /// muestran las opciones cuyo <c>parent</c> este entre las selecciones del nivel padre. Los niveles
    /// con fuente externa (sin <c>options</c> inline) se resuelven en runtime aparte (no aqui).</summary>
    public static IReadOnlyList<CascadeOption> VisibleOptions(
        CascadeLevel level,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
    {
        if (level.Options is null || level.Options.Count == 0)
        {
            return Array.Empty<CascadeOption>();
        }
        if (string.IsNullOrWhiteSpace(level.DependsOn))
        {
            return level.Options;
        }

        var parentSel = selections.TryGetValue(level.DependsOn, out var p) ? p : Array.Empty<string>();
        var parentSet = new HashSet<string>(parentSel, StringComparer.Ordinal);
        return level.Options.Where(o => o.Parent is not null && parentSet.Contains(o.Parent)).ToList();
    }

    /// <summary>Una tabla del paso final: su clave (keyBy), el juego de columnas resuelto, los productos
    /// elegidos que la alimentan (en orden de catalogo) y los valores alimentados por columna
    /// (ej. "proceso" -> [laser, mig]).</summary>
    public sealed record CascadeTableSpec(
        string Key,
        IReadOnlyList<KeyValuePair<string, string>> KeyParts,
        string ColumnSet,
        IReadOnlyList<string> ProductOptionIds,
        IReadOnlyList<string> ProductLabels,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Feeds);

    /// <summary>Tablas que deben existir dado lo seleccionado. Una por combinacion de keyBy (ej.
    /// portafolio + columnSet): productos con la misma clave comparten tabla y acumulan su nombre.</summary>
    public static IReadOnlyList<CascadeTableSpec> DesiredTables(
        CascadeConfig cfg,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
    {
        var index = BuildIndex(cfg);
        var result = new List<CascadeTableSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var level in cfg.Levels.Where(l => l.OpensTable))
        {
            var selected = selections.TryGetValue(level.Key, out var s) ? s : Array.Empty<string>();
            var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);

            // Opciones del nivel-tabla en ORDEN de catalogo, filtradas a lo seleccionado.
            var options = (level.Options ?? new List<CascadeOption>())
                .Where(o => selectedSet.Contains(o.Id)).ToList();

            foreach (var opt in options)
            {
                var columnSet = CascadeConfig.ResolveColumnSet(cfg, opt.Id);
                if (string.IsNullOrWhiteSpace(columnSet))
                {
                    continue;
                }

                var keyParts = new List<KeyValuePair<string, string>>();
                foreach (var comp in cfg.Table.KeyBy)
                {
                    var value = string.Equals(comp, "columnSet", StringComparison.OrdinalIgnoreCase)
                        ? columnSet
                        : AncestorAtLevel(index, opt, comp) ?? opt.Id;
                    keyParts.Add(new KeyValuePair<string, string>(comp, value));
                }
                var key = string.Join("#", keyParts.Select(kp => kp.Value));
                if (!seen.Add(key))
                {
                    continue;
                }

                // Productos de ESTA tabla: los seleccionados del nivel-tabla que resuelven a la misma clave.
                var products = options.Where(o => TableKeyOf(cfg, index, o) == key).ToList();
                var feeds = FeedsForProducts(cfg, index, selections, products.Select(o => o.Id).ToHashSet(StringComparer.Ordinal));

                result.Add(new CascadeTableSpec(
                    key,
                    keyParts,
                    columnSet!,
                    products.Select(o => o.Id).ToList(),
                    products.Select(o => o.Label).ToList(),
                    feeds));
            }
        }

        return result;
    }

    /// <summary>Clave keyBy de un producto (para agrupar productos en la misma tabla).</summary>
    public static string TableKeyOf(CascadeConfig cfg, IReadOnlyDictionary<string, LevelOption> index, CascadeOption opt)
    {
        var columnSet = CascadeConfig.ResolveColumnSet(cfg, opt.Id) ?? "";
        var parts = cfg.Table.KeyBy.Select(comp =>
            string.Equals(comp, "columnSet", StringComparison.OrdinalIgnoreCase)
                ? columnSet
                : AncestorAtLevel(index, opt, comp) ?? opt.Id);
        return string.Join("#", parts);
    }

    /// <summary>Valores alimentados por columna para un conjunto de productos: por cada nivel con
    /// <c>feedsColumn</c>, junta las opciones seleccionadas cuyo padre este entre esos productos, en orden
    /// de catalogo y sin repetir. Ej. "proceso" -> [laser, mig].</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FeedsForProducts(
        CascadeConfig cfg,
        IReadOnlyDictionary<string, LevelOption> index,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections,
        HashSet<string> productIds)
    {
        var feeds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var level in cfg.Levels.Where(l => !string.IsNullOrWhiteSpace(l.FeedsColumn) && l.Options is not null))
        {
            var selected = selections.TryGetValue(level.Key, out var s) ? s : Array.Empty<string>();
            var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);
            var values = new List<string>();
            foreach (var o in level.Options!)
            {
                if (!selectedSet.Contains(o.Id) || o.Parent is null || !productIds.Contains(o.Parent))
                {
                    continue;
                }
                if (!values.Contains(o.Label))
                {
                    values.Add(o.Label);
                }
            }
            if (values.Count > 0)
            {
                feeds[level.FeedsColumn!] = values;
            }
        }
        return feeds;
    }

    /// <summary>Sube por la cadena de <c>parent</c> hasta la opcion que pertenece a <paramref name="levelKey"/>.</summary>
    private static string? AncestorAtLevel(IReadOnlyDictionary<string, LevelOption> index, CascadeOption opt, string levelKey)
    {
        var current = opt;
        var guard = 0;
        while (current is not null && guard++ < 50)
        {
            if (index.TryGetValue(current.Id, out var lo) && string.Equals(lo.LevelKey, levelKey, StringComparison.Ordinal))
            {
                return current.Id;
            }
            current = current.Parent is not null && index.TryGetValue(current.Parent, out var pp) ? pp.Option : null;
        }
        return null;
    }

    /// <summary>Una opcion y a que nivel pertenece.</summary>
    public sealed record LevelOption(CascadeOption Option, string LevelKey);

    /// <summary>Indice global optionId -> (opcion, nivel).</summary>
    public static IReadOnlyDictionary<string, LevelOption> BuildIndex(CascadeConfig cfg)
    {
        var map = new Dictionary<string, LevelOption>(StringComparer.Ordinal);
        foreach (var lvl in cfg.Levels)
        {
            if (lvl.Options is null)
            {
                continue;
            }
            foreach (var op in lvl.Options)
            {
                map[op.Id] = new LevelOption(op, lvl.Key);
            }
        }
        return map;
    }
}

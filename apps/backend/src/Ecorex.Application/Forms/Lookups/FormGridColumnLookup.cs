using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Configuracion de lookup de UNA COLUMNA de tabla (GridDetail). Hasta ahora el lookup con
/// autollenado solo existia a nivel de CAMPO (FormQuestionDto.SourceKind + AutofillMapJson); esto
/// lo lleva a la columna reusando la MISMA capa (<see cref="IFormLookupService"/> y sus
/// adaptadores Item/Tercero/DataContainer), sin ningun codigo especifico de inventario.
///
/// <para><see cref="ValueField"/> es la diferencia importante con el lookup de campo: a nivel de
/// campo se guarda el id de la entidad, pero en una tabla de cotizacion la celda debe guardar una
/// CLAVE LEGIBLE que el asesor teclea y lee (el SKU). Si viene vacio se guarda el id, igual que el
/// campo.</para>
///
/// <para><see cref="Autofill"/> mapea campoDeLaFuente -&gt; idDeColumnaDestino DENTRO DE LA MISMA
/// FILA. Lo copiado es un SNAPSHOT y queda EDITABLE: el asesor puede ajustar costo, marca o
/// proveedor en una cotizacion puntual sin tocar el catalogo. NO es un vinculo vivo, por lo que
/// nada vuelve a leer la fuente despues de elegir (decision del usuario).</para>
/// </summary>
public sealed record FormGridLookupConfig(
    FormSourceKind SourceKind,
    string? SourceRef,
    string? DisplayField,
    string? ValueField,
    string? FilterJson,
    IReadOnlyDictionary<string, string> Autofill,
    // Como se ofrece la celda: Autocomplete (teclear y filtrar, default), Dropdown (lista con el
    // catalogo) o Modal (buscador grande). Default Autocomplete para no alterar lo ya configurado.
    FormFieldPresentation Presentation = FormFieldPresentation.Autocomplete,
    // Campo secundario a mostrar junto al Display en cada resultado (ej. el SKU), para distinguir
    // items con nombres parecidos. Null = usa la clave (valueField / KeyOf). Vacio o = Display: no se
    // muestra.
    string? SubLabel = null)
{
    /// <summary>Campos que hay que pedirle a la fuente: el de mostrar, el de la clave y los origenes del autollenado.</summary>
    public IReadOnlyList<string> Fields()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(DisplayField)) { set.Add(DisplayField!); }
        if (!string.IsNullOrWhiteSpace(ValueField)) { set.Add(ValueField!); }
        if (!string.IsNullOrWhiteSpace(SubLabel)) { set.Add(SubLabel!); }
        foreach (var source in Autofill.Keys) { set.Add(source); }
        return set.ToList();
    }

    /// <summary>Clave que se guarda en la celda para un resultado: <see cref="ValueField"/> o, si no hay, el id.</summary>
    public string KeyOf(FormLookupItem item)
        => !string.IsNullOrWhiteSpace(ValueField) && item.Fields.TryGetValue(ValueField!, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v!
            : item.Value;
}

/// <summary>
/// Comprobacion de existencias de una columna: esta columna trae el stock disponible y se compara
/// contra la columna <see cref="Against"/> (la cantidad pedida) para avisar en la fila cuando no
/// alcanza. Es declarativo y no bloquea nada: se puede cotizar sobre pedido.
/// </summary>
public sealed record FormGridStockCheck(string Against);

/// <summary>
/// Auto-resolucion por CLAVE COMPUESTA (tipo VLOOKUP/INDEX-MATCH). A diferencia del lookup dirigido
/// por seleccion (<see cref="FormGridLookupConfig"/>), esta columna se CALCULA sola: matchea una fila
/// de la fuente por 1+ columnas (<see cref="Match"/>: columnaFuente -&gt; ref de celda de la fila,
/// ej. "{tipo_lamina}") y devuelve <see cref="ReturnField"/>. Se re-resuelve cuando cambian sus
/// dependencias (como un calc) y es de solo lectura (snapshot editable). <see cref="When"/> es una
/// guarda opcional (refDeCelda -&gt; valor exacto): si no se cumple, la celda queda vacia (0). El match
/// del contrato actual es EXACTO (decision del usuario): compara numerico si ambos lados son numero,
/// si no texto (case-insensitive), asi "3"=="3.0" y "&lt;3"=="&lt;3".
/// </summary>
public sealed record FormGridResolveConfig(
    FormSourceKind SourceKind,
    string? SourceRef,
    IReadOnlyDictionary<string, string> Match,
    string ReturnField,
    IReadOnlyDictionary<string, string> When,
    // Modo HIBRIDO (allowManual): la matriz autollena donde HAY match; donde NO hay match la celda queda
    // EDITABLE y el valor tecleado no se borra (ni en el recalculo autoritativo al guardar). Default false
    // = comportamiento clasico (readonly, se vacia si no hay match).
    bool AllowManual = false);

/// <summary>
/// CAP 2 (referencia ENTRE GRILLAS del mismo registro): esta columna se calcula sola trayendo un valor
/// desde OTRA grilla del MISMO formulario. Dos modos:
///  - "vlookup": empareja una fila de la grilla origen por <see cref="Match"/> (columnaOrigen -&gt; ref de
///    celda de ESTA fila, ej. "{item}") y devuelve <see cref="ValueField"/> de esa fila.
///  - "sumif": AGRUPA la grilla origen por la(s) columna(s) de <see cref="Match"/> y agrega
///    (<see cref="Agg"/>, default Sum) <see cref="ValueField"/> por grupo, devolviendo el subtotal del grupo
///    cuya clave coincide con los valores de ESTA fila. Se apoya en el agregado agrupado (CAP 1).
/// La grilla origen debe recalcularse ANTES (orden por dependencias). El match es EXACTO con la misma
/// normalizacion que el resolve (numerico si ambos lados son numero, si no texto case-insensitive).
/// </summary>
public sealed record FormGridCrossRef(
    string Grid,                                 // field code de la pregunta GridDetail origen
    string Mode,                                 // "vlookup" | "sumif"
    string ValueField,                           // columna de la grilla origen a devolver/agregar
    Ecorex.Domain.Enums.FormAggregate Agg,       // solo sumif (Sum/Count/Avg/Min/Max)
    IReadOnlyDictionary<string, string> Match)   // columnaOrigen -> ref de celda de esta fila (ej. "{item}")
{
    public bool IsSumIf => string.Equals(Mode, "sumif", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Extras de una columna de tabla que NO viven en FormGridColumn (calculo/agregado): lookup con
/// autollenado, valor por defecto al crear la fila, comprobacion de existencias, auto-resolucion
/// multi-clave y referencia CROSS-GRID (CAP 2). Se parsean del MISMO OptionsJson de la pregunta, en
/// paralelo a FormGridCalculator.ParseColumns, para no mezclar responsabilidades ni tocar el calculador.
/// </summary>
public sealed record FormGridColumnExtras(
    string Id,
    FormGridLookupConfig? Lookup,
    string? Default,
    FormGridStockCheck? StockCheck,
    FormGridResolveConfig? Resolve = null,
    FormGridCrossRef? CrossRef = null);

/// <summary>
/// Parseo de los extras de columna del OptionsJson. Defensivo: cualquier columna mal formada se
/// ignora y la tabla sigue funcionando como texto plano (las definiciones viejas [{id,label}] no
/// declaran nada de esto y siguen valiendo).
/// </summary>
public static class FormGridColumnLookupParser
{
    /// <summary>Extras por id de columna. Diccionario vacio si el JSON no es un array valido.</summary>
    public static IReadOnlyDictionary<string, FormGridColumnExtras> Parse(string? optionsJson)
    {
        var map = new Dictionary<string, FormGridColumnExtras>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(optionsJson)) { return map; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return map; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var id = el.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) { continue; }

                var lookup = ParseLookup(el);
                var def = el.TryGetProperty("default", out var pd) ? ReadScalar(pd) : null;
                var stock = ParseStockCheck(el);
                var resolve = ParseResolve(el);
                var crossRef = ParseCrossRef(el);
                if (lookup is null && def is null && stock is null && resolve is null && crossRef is null) { continue; }

                map[id!] = new FormGridColumnExtras(id!, lookup, def, stock, resolve, crossRef);
            }
        }
        catch (JsonException) { /* extras invalidos: la tabla se comporta como texto plano */ }
        return map;
    }

    private static FormGridLookupConfig? ParseLookup(JsonElement col)
    {
        if (!col.TryGetProperty("lookup", out var lk) || lk.ValueKind != JsonValueKind.Object) { return null; }

        // "source" acepta el nombre del enum (Item / Tercero / DataContainer). Si no se reconoce no
        // se arma lookup: mejor una columna de texto que una que consulta la fuente equivocada.
        var sourceName = lk.TryGetProperty("source", out var ps) ? ps.GetString() : null;
        if (!Enum.TryParse<FormSourceKind>(sourceName, ignoreCase: true, out var kind) || kind == FormSourceKind.Options)
        {
            return null;
        }

        var autofill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lk.TryGetProperty("autofill", out var af) && af.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in af.EnumerateObject())
            {
                var target = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(target)) { autofill[p.Name] = target!; }
            }
        }

        // El filtro se conserva como JSON crudo: lo interpreta cada adaptador de fuente, no la UI.
        string? filterJson = null;
        if (lk.TryGetProperty("filter", out var pf) && pf.ValueKind == JsonValueKind.Object)
        {
            filterJson = pf.GetRawText();
        }

        return new FormGridLookupConfig(
            kind,
            Trimmed(lk, "sourceRef"),
            Trimmed(lk, "displayField"),
            Trimmed(lk, "valueField"),
            filterJson,
            autofill,
            ParsePresentation(Trimmed(lk, "presentation")),
            Trimmed(lk, "subLabel"));
    }

    /// <summary>presentation: "list"/"dropdown" -> Dropdown, "modal" -> Modal, cualquier otro -> Autocomplete.</summary>
    private static FormFieldPresentation ParsePresentation(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "list" or "dropdown" => FormFieldPresentation.Dropdown,
        "modal" => FormFieldPresentation.Modal,
        _ => FormFieldPresentation.Autocomplete,
    };

    private static FormGridStockCheck? ParseStockCheck(JsonElement col)
    {
        if (!col.TryGetProperty("stockCheck", out var sc) || sc.ValueKind != JsonValueKind.Object) { return null; }
        var against = Trimmed(sc, "against");
        return string.IsNullOrWhiteSpace(against) ? null : new FormGridStockCheck(against!);
    }

    private static FormGridResolveConfig? ParseResolve(JsonElement col)
    {
        if (!col.TryGetProperty("resolve", out var rv) || rv.ValueKind != JsonValueKind.Object) { return null; }

        var sourceName = rv.TryGetProperty("source", out var ps) ? ps.GetString() : null;
        if (!Enum.TryParse<FormSourceKind>(sourceName, ignoreCase: true, out var kind) || kind == FormSourceKind.Options)
        {
            return null;
        }
        var returnField = Trimmed(rv, "return");
        if (string.IsNullOrWhiteSpace(returnField)) { return null; }

        var match = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rv.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in m.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(v)) { match[p.Name] = v!; }
            }
        }
        if (match.Count == 0) { return null; }

        // 'when' opcional: refDeCelda -> valor esperado (exacto). Sin 'when' la resolucion siempre corre.
        var when = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rv.TryGetProperty("when", out var w) && w.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in w.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (v is not null) { when[p.Name] = v; }
            }
        }

        // 'allowManual' opcional (default false): modo hibrido VLOOKUP + captura manual.
        var allowManual = rv.TryGetProperty("allowManual", out var am)
            && (am.ValueKind == JsonValueKind.True
                || (am.ValueKind == JsonValueKind.String && bool.TryParse(am.GetString(), out var amb) && amb));

        return new FormGridResolveConfig(kind, Trimmed(rv, "sourceRef"), match, returnField!, when, allowManual);
    }

    /// <summary>CAP 2: parsea la referencia cross-grid del options_json de la columna
    /// ("crossGrid": {"grid":"lineas_apu","mode":"sumif","valueField":"parcial","agg":"Sum","match":{"grupo":"{grupo}"}}).</summary>
    private static FormGridCrossRef? ParseCrossRef(JsonElement col)
    {
        if (!col.TryGetProperty("crossGrid", out var cg) || cg.ValueKind != JsonValueKind.Object) { return null; }
        var grid = Trimmed(cg, "grid");
        var valueField = Trimmed(cg, "valueField");
        if (string.IsNullOrWhiteSpace(grid) || string.IsNullOrWhiteSpace(valueField)) { return null; }

        var mode = (Trimmed(cg, "mode") ?? "vlookup").ToLowerInvariant();
        if (mode != "vlookup" && mode != "sumif") { mode = "vlookup"; }

        var agg = Ecorex.Domain.Enums.FormAggregate.Sum;
        if (cg.TryGetProperty("agg", out var pa) && Enum.TryParse<Ecorex.Domain.Enums.FormAggregate>(pa.GetString(), ignoreCase: true, out var parsedAgg))
        {
            agg = parsedAgg;
        }

        var match = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (cg.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in m.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(v)) { match[p.Name] = v!; }
            }
        }
        if (match.Count == 0) { return null; }

        return new FormGridCrossRef(grid!, mode, valueField!, agg, match);
    }

    private static string? Trimmed(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) { return null; }
        var s = p.GetString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>El default se admite como texto o numero (1 y "1" son lo mismo en una celda).</summary>
    private static string? ReadScalar(JsonElement p) => p.ValueKind switch
    {
        JsonValueKind.String => p.GetString(),
        JsonValueKind.Number => p.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };
}
